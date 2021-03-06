using UnityEngine;
using System.Collections;
using Chisel.Core;
using System.Collections.Generic;
using System;
using UnityEngine.Rendering;
using System.Transactions;
using UnityEngine.Profiling;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using System.Runtime.InteropServices;

namespace Chisel.Components
{        
    public enum DrawModeFlags
    {
        None            = 0,
        Default         = None,
        HideRenderables = 1,
        ShowColliders   = 2,
        ShowCasters     = 4,
        ShowShadowOnly  = 8,
        ShowReceivers   = 16,
        ShowCulled      = 32,
        ShowDiscarded   = 64,
    }

    //
    // 1. figure out what you where trying to do here, and remove need for the dictionary
    // 2. then do the same for the rendering equiv.
    // 3. separate building/updating the components from building a combined (phsyics)materials/mesh/rendersetting
    // 4. move colliders to single gameobject
    // 5. build meshes with submeshes, have a fixed number of meshes. one mesh per renderingsetting type (shadow-only etc.)
    // 6. have a secondary version of these fixed number of meshes that has partial meshes & use those for rendering in chiselmodel
    // 7. have a way to identify which triangles belong to which brush. so we can build partial meshes
    // 8. profit!
    //
    [Serializable]
    public class ChiselGeneratedObjects
    {
        public const string kGeneratedContainerName     = "‹[generated]›";
        public const int kGeneratedMeshRenderCount = 8;
        public const int kGeneratedMeshRendererCount = 5;
        public static readonly string[] kGeneratedMeshRendererNames = new string[]
        {
            null,                                                   // 0 (invalid option)
            "‹[generated-Renderable]›",                             // 1
            "‹[generated-CastShadows]›",                            // 2 (Shadow-Only)
            "‹[generated-Renderable|CastShadows]›",                 // 3
            null,                                                   // 4 (invalid option)
            "‹[generated-Renderable|ReceiveShadows]›",              // 5
            null,                                                   // 6 (invalid option)
            "‹[generated-Renderable|CastShadows|ReceiveShadows]›"   // 7
        };

        public const int kDebugHelperCount = 6;
        public static readonly string[] kGeneratedDebugRendererNames = new string[kDebugHelperCount]
        {
            "‹[debug-Discarded]›",                                  // LayerUsageFlags.None
            "‹[debug-CastShadows]›",                                // LayerUsageFlags.RenderableCastShadows
            "‹[debug-ShadowOnly]›",                                 // LayerUsageFlags.CastShadows
            "‹[debug-ReceiveShadows]›",                             // LayerUsageFlags.RenderableReceiveShadows
            "‹[debug-Collidable]›",                                 // LayerUsageFlags.Collidable
            "‹[debug-Culled]›"                                      // LayerUsageFlags.Culled
        };
        public static readonly (LayerUsageFlags, LayerUsageFlags)[] kGeneratedDebugRendererFlags = new (LayerUsageFlags, LayerUsageFlags)[kDebugHelperCount]
        {
            ( LayerUsageFlags.None                  , LayerUsageFlags.Renderable),              // is explicitly set to "not visible"
            ( LayerUsageFlags.RenderCastShadows     , LayerUsageFlags.RenderCastShadows),       // casts Shadows and is renderered
            ( LayerUsageFlags.CastShadows           , LayerUsageFlags.RenderCastShadows),       // casts Shadows and is NOT renderered (shadowOnly)
            ( LayerUsageFlags.RenderReceiveShadows  , LayerUsageFlags.RenderReceiveShadows),    // any surface that receives shadows (must be rendered)
            ( LayerUsageFlags.Collidable            , LayerUsageFlags.Collidable),              // collider surfaces
            ( LayerUsageFlags.Culled                , LayerUsageFlags.Culled)                   // all surfaces removed by the CSG algorithm
        };
        public static readonly DrawModeFlags[] kGeneratedDebugShowFlags = new DrawModeFlags[kDebugHelperCount]
        {
            DrawModeFlags.ShowDiscarded,
            DrawModeFlags.ShowCasters,
            DrawModeFlags.ShowShadowOnly,
            DrawModeFlags.ShowReceivers,
            DrawModeFlags.ShowColliders,
            DrawModeFlags.ShowCulled
        };
        public const string kGeneratedMeshColliderName	= "‹[generated-Collider]›";

        public GameObject               generatedDataContainer;
        public GameObject               colliderContainer;
        public ChiselColliderObjects[]  colliders;

        public ChiselRenderObjects[]    renderables;
        public MeshRenderer[]           meshRenderers;

        public ChiselRenderObjects[]    debugHelpers;
        public MeshRenderer[]           debugMeshRenderers;

        public VisibilityState          visibilityState             = VisibilityState.Unknown;
        public bool                     needVisibilityMeshUpdate    = false;
        
        private ChiselGeneratedObjects() { }

        public static ChiselGeneratedObjects Create(GameObject parentGameObject)
        {
            var parentTransform     = parentGameObject.transform;

            // Make sure there's not a dangling container out there from a previous version
            var existingContainer   = parentTransform.FindChildByName(kGeneratedContainerName);
            ChiselObjectUtility.SafeDestroy(existingContainer, ignoreHierarchyEvents: true);

            var gameObjectState     = GameObjectState.Create(parentGameObject);
            var container           = ChiselObjectUtility.CreateGameObject(kGeneratedContainerName, parentTransform, gameObjectState);
            var containerTransform  = container.transform;
            var colliderContainer   = ChiselObjectUtility.CreateGameObject(kGeneratedMeshColliderName, containerTransform, gameObjectState);

            Debug.Assert((int)LayerUsageFlags.Renderable     == 1);
            Debug.Assert((int)LayerUsageFlags.CastShadows    == 2);
            Debug.Assert((int)LayerUsageFlags.ReceiveShadows == 4);
            Debug.Assert((int)LayerUsageFlags.RenderReceiveCastShadows == (1|2|4));

            var renderables = new ChiselRenderObjects[]
            {
                new ChiselRenderObjects() { invalid = true },
                ChiselRenderObjects.Create(kGeneratedMeshRendererNames[1], containerTransform, gameObjectState, LayerUsageFlags.Renderable                               ),
                ChiselRenderObjects.Create(kGeneratedMeshRendererNames[2], containerTransform, gameObjectState, LayerUsageFlags.CastShadows                              ),
                ChiselRenderObjects.Create(kGeneratedMeshRendererNames[3], containerTransform, gameObjectState, LayerUsageFlags.Renderable | LayerUsageFlags.CastShadows ),
                new ChiselRenderObjects() { invalid = true },
                ChiselRenderObjects.Create(kGeneratedMeshRendererNames[5], containerTransform, gameObjectState, LayerUsageFlags.Renderable |                               LayerUsageFlags.ReceiveShadows),
                new ChiselRenderObjects() { invalid = true },
                ChiselRenderObjects.Create(kGeneratedMeshRendererNames[7], containerTransform, gameObjectState, LayerUsageFlags.Renderable | LayerUsageFlags.CastShadows | LayerUsageFlags.ReceiveShadows),
            };

            var meshRenderers = new MeshRenderer[]
            {
                renderables[1].meshRenderer,
                renderables[2].meshRenderer,
                renderables[3].meshRenderer,
                renderables[5].meshRenderer,
                renderables[7].meshRenderer
            };

            renderables[1].invalid = false;
            renderables[2].invalid = false;
            renderables[3].invalid = false;
            renderables[5].invalid = false;
            renderables[7].invalid = false;

            var debugHelpers = new ChiselRenderObjects[kDebugHelperCount];
            var debugMeshRenderers = new MeshRenderer[kDebugHelperCount];
            for (int i = 0; i < kDebugHelperCount; i++)
            {
                debugHelpers[i] = ChiselRenderObjects.Create(kGeneratedDebugRendererNames[i], containerTransform, gameObjectState, kGeneratedDebugRendererFlags[i].Item1, debugHelperRenderer: true);
                debugMeshRenderers[i] = debugHelpers[0].meshRenderer;
                debugHelpers[i].invalid = false;
            }

            var result = new ChiselGeneratedObjects
            {
                generatedDataContainer  = container,
                colliderContainer       = colliderContainer,
                colliders               = new ChiselColliderObjects[0],
                renderables             = renderables,
                meshRenderers           = meshRenderers,
                debugHelpers            = debugHelpers,
                debugMeshRenderers      = debugMeshRenderers
            };

            Debug.Assert(IsValid(result));

            return result;
        }

        public void Destroy()
        {
            if (!generatedDataContainer)
                return;

            if (colliders != null)
            {
                foreach (var collider in colliders)
                {
                    if (collider != null)
                        collider.Destroy();
                }
                colliders = null;
            }
            if (renderables != null)
            {
                foreach (var renderable in renderables)
                {
                    if (renderable != null)
                        renderable.Destroy();
                }
                renderables = null;
            }
            if (debugHelpers != null)
            {
                foreach (var debugHelper in debugHelpers)
                {
                    if (debugHelper != null)
                        debugHelper.Destroy();
                }
                debugHelpers = null;
            }
            ChiselObjectUtility.SafeDestroy(colliderContainer, ignoreHierarchyEvents: true);
            ChiselObjectUtility.SafeDestroy(generatedDataContainer, ignoreHierarchyEvents: true);
            generatedDataContainer  = null;
            colliderContainer       = null;

            meshRenderers       = null;
            debugMeshRenderers  = null;
        }

        public void DestroyWithUndo()
        {
            if (!generatedDataContainer)
                return;

            if (colliders != null)
            {
                foreach (var collider in colliders)
                {
                    if (collider != null)
                        collider.DestroyWithUndo();
                }
            }
            if (renderables != null)
            {
                foreach (var renderable in renderables)
                {
                    if (renderable != null)
                        renderable.DestroyWithUndo();
                }
            }
            if (debugHelpers != null)
            {
                foreach (var debugHelper in debugHelpers)
                {
                    if (debugHelper != null)
                        debugHelper.DestroyWithUndo();
                }
            }
            ChiselObjectUtility.SafeDestroyWithUndo(colliderContainer, ignoreHierarchyEvents: true);
            ChiselObjectUtility.SafeDestroyWithUndo(generatedDataContainer, ignoreHierarchyEvents: true);
        }

        public void RemoveContainerFlags()
        {
            if (colliders != null)
            {
                foreach (var collider in colliders)
                {
                    if (collider != null)
                        collider.RemoveContainerFlags();
                }
            }
            if (renderables != null)
            {
                foreach (var renderable in renderables)
                {
                    if (renderable != null)
                        renderable.RemoveContainerFlags();
                }
            }
            if (debugHelpers != null)
            {
                foreach (var debugHelper in debugHelpers)
                {
                    if (debugHelper != null)
                        debugHelper.RemoveContainerFlags();
                }
            }
            ChiselObjectUtility.RemoveContainerFlags(colliderContainer);
            ChiselObjectUtility.RemoveContainerFlags(generatedDataContainer);
        }

        public static bool IsValid(ChiselGeneratedObjects satelliteObjects)
        {
            if (satelliteObjects == null)
                return false;

            if (!satelliteObjects.generatedDataContainer)
                return false;

            if (!satelliteObjects.colliderContainer ||
                satelliteObjects.colliders == null)   // must be an array, even if 0 length
                return false;

            if (satelliteObjects.renderables == null ||
                satelliteObjects.renderables.Length != kGeneratedMeshRenderCount ||
                satelliteObjects.meshRenderers == null ||
                satelliteObjects.meshRenderers.Length != kGeneratedMeshRendererCount)
                return false;

            if (satelliteObjects.debugHelpers == null ||
                satelliteObjects.debugHelpers.Length != kDebugHelperCount ||
                satelliteObjects.debugMeshRenderers == null ||
                satelliteObjects.debugMeshRenderers.Length != kDebugHelperCount)
                return false;

            // These queries are valid, and should never be null (We don't care about the other queries)
            if (satelliteObjects.renderables[1] == null ||
                satelliteObjects.renderables[2] == null ||
                satelliteObjects.renderables[3] == null ||
                satelliteObjects.renderables[5] == null ||
                satelliteObjects.renderables[7] == null)
                return false;

            // These queries are valid, and should never be null (We don't care about the other queries)
            for (int i = 0; i < kDebugHelperCount;i++)
            { 
                if (satelliteObjects.debugHelpers[i] == null)
                    return false;
            }
            
            satelliteObjects.renderables[0].invalid = true;
            satelliteObjects.renderables[1].invalid = false;
            satelliteObjects.renderables[2].invalid = false;
            satelliteObjects.renderables[3].invalid = false;
            satelliteObjects.renderables[4].invalid = true;
            satelliteObjects.renderables[5].invalid = false;
            satelliteObjects.renderables[6].invalid = true;
            satelliteObjects.renderables[7].invalid = false;

            for (int i = 0; i < kDebugHelperCount; i++)
                satelliteObjects.debugHelpers[i].invalid = false;

            for (int i = 0; i < satelliteObjects.renderables.Length; i++)
            {
                if (satelliteObjects.renderables[i] == null ||
                    satelliteObjects.renderables[i].invalid)
                    continue;
                if (!ChiselRenderObjects.IsValid(satelliteObjects.renderables[i]))
                    return false;
            }

            for (int i = 0; i < satelliteObjects.debugHelpers.Length; i++)
            {
                if (satelliteObjects.debugHelpers[i] == null)
                    continue;
                if (!ChiselRenderObjects.IsValid(satelliteObjects.debugHelpers[i]))
                    return false;
            }

            for (int i = 0; i < satelliteObjects.colliders.Length; i++)
            {
                if (!ChiselColliderObjects.IsValid(satelliteObjects.colliders[i]))
                    return false;
            }

            return true;
        }

        public bool HasLightmapUVs
        {
            get
            {
#if UNITY_EDITOR
                if (renderables == null)
                    return false;

                for (int i = 0; i < renderables.Length; i++)
                {
                    if (renderables[i] == null || 
                        renderables[i].invalid)
                        continue;
                    if (renderables[i].HasLightmapUVs)
                        return true;
                }
#endif
                return false;
            }
        }

        static bool[] renderMeshUpdated = null;
        static bool[] helperMeshUpdated = null;

        static readonly HashSet<(ChiselGeneratedObjects, ChiselModel)> s_GeneratedObjects = new HashSet<(ChiselGeneratedObjects, ChiselModel)>();

        Dictionary<ChiselModel, GameObjectState>        gameObjectStates    = new Dictionary<ChiselModel, GameObjectState>();
        List<ChiselPhysicsObjectUpdate>                 physicsUpdates      = new List<ChiselPhysicsObjectUpdate>();
        List<ChiselRenderObjectUpdate>                  renderUpdates       = new List<ChiselRenderObjectUpdate>();
        List<ChiselColliderObjects>                     colliderObjects     = new List<ChiselColliderObjects>();
        List<Mesh>                                      foundMeshes         = new List<Mesh>();
        Mesh.MeshDataArray                              dataArray;
        int                                             colliderCount       = 0;

        public static void BeginMeshUpdates()
        {
            s_GeneratedObjects.Clear();
        }

        public JobHandle UpdateMeshes(ChiselModel model, GameObject parentGameObject, ref VertexBufferContents vertexBufferContents, JobHandle dependencies)
        {
            gameObjectStates.Clear();
            physicsUpdates.Clear();
            renderUpdates.Clear();
            colliderObjects.Clear();
            foundMeshes.Clear();
            colliderCount = 0;

            GameObjectState gameObjectState;
            { 
                Profiler.BeginSample("Setup");
                var parentTransform     = parentGameObject.transform;
                gameObjectState         = GameObjectState.Create(parentGameObject);
                ChiselObjectUtility.UpdateContainerFlags(generatedDataContainer, gameObjectState);

                var containerTransform  = generatedDataContainer.transform;
                var colliderTransform   = colliderContainer.transform;

                // Make sure we're always a child of the model
                ChiselObjectUtility.ResetTransform(containerTransform, requiredParent: parentTransform);
                ChiselObjectUtility.ResetTransform(colliderTransform, requiredParent: containerTransform);
                ChiselObjectUtility.UpdateContainerFlags(colliderContainer, gameObjectState);

                for (int i = 0; i < renderables.Length; i++)
                {
                    if (renderables[i] == null || renderables[i].invalid)
                        continue;

                    bool isRenderable = (renderables[i].query & LayerUsageFlags.Renderable) == LayerUsageFlags.Renderable;
                    var renderableContainer = renderables[i].container;
                    ChiselObjectUtility.UpdateContainerFlags(renderableContainer, gameObjectState, isRenderable: isRenderable);
                    ChiselObjectUtility.ResetTransform(renderableContainer.transform, requiredParent: containerTransform);
                }
            
                for (int i = 0; i < debugHelpers.Length; i++)
                {
                    if (debugHelpers[i] == null || debugHelpers[i].invalid)
                        continue;
                    var renderableContainer = debugHelpers[i].container;
                    ChiselObjectUtility.UpdateContainerFlags(renderableContainer, gameObjectState, isRenderable: true, debugHelperRenderer: true);
                    ChiselObjectUtility.ResetTransform(renderableContainer.transform, requiredParent: containerTransform);
                }
                Profiler.EndSample();
                gameObjectStates.Add(model, gameObjectState);

                Profiler.BeginSample("meshUpdated");
                if (helperMeshUpdated == null || helperMeshUpdated.Length < debugHelpers.Length)
                    helperMeshUpdated = new bool[debugHelpers.Length];
                Array.Clear(helperMeshUpdated, 0, helperMeshUpdated.Length);
                if (renderMeshUpdated == null || renderMeshUpdated.Length < renderables.Length)
                    renderMeshUpdated = new bool[renderables.Length];
                Array.Clear(renderMeshUpdated, 0, renderMeshUpdated.Length);
                Profiler.EndSample();
            }



            dependencies.Complete(); // because of dependency on vertexBufferContents 
                                     //                 => could potentially be jobified
                                     //            dependency on count of sections in vertexBufferContents which are colliders 
                                     //                 => theoretical maximum could potentially be determined in advance (see below)
                                     //            dependency on Mesh.AllocateWritableMeshData(number_of_meshes_to_create) 
                                     //                 => CAN NOT currently be worked around b/c we cannot know in advance which meshes would get modified
                                     //                     (see below)

            // TODO: - find a way to keep the list of used physicMaterials in each particular model
            //       - keep a list of meshes around, one for each physicMaterial
            //       - the number of meshes is now fixed as long as no physicMaterial is added/removed
            //       - the number of meshColliders could be the same size, just some meshColliders enabled/disabled
            //       - our number of meshes (colliders + renderers) is now predictable
            //
            // PROBLEM: Still wouldn't know in advance _which_ of these meshes would actually not change at all ...
            //          ... and don't want to change ALL of them, ALL the time. 
            //          So the mesh count would still be an unknown until we do a Complete

            var currentJobHandle = (JobHandle)default;

            Debug.Assert(LayerParameterIndex.LayerParameter1 < LayerParameterIndex.LayerParameter2);
            Debug.Assert((LayerParameterIndex.LayerParameter1 + 1) == LayerParameterIndex.LayerParameter2);

            Debug.Assert(!vertexBufferContents.meshDescriptions.IsCreated ||
                         vertexBufferContents.meshDescriptions.Length == 0 ||
                         vertexBufferContents.meshDescriptions[0].meshQuery.LayerParameterIndex >= LayerParameterIndex.None);

            Profiler.BeginSample("Init.RenderUpdates");
            if (vertexBufferContents.meshDescriptions.IsCreated)
            {
                for (int i = 0; i < vertexBufferContents.subMeshSections.Length; i++)
                {
                    var subMeshSection = vertexBufferContents.subMeshSections[i];
                    if (subMeshSection.meshQuery.LayerParameterIndex == LayerParameterIndex.None)
                    {
                        int helperIndex = Array.IndexOf(kGeneratedDebugRendererFlags, (subMeshSection.meshQuery.LayerQuery, subMeshSection.meshQuery.LayerQueryMask));
                        if (helperIndex == -1)
                        {
                            Debug.Assert(false, $"Invalid helper query used (query: {subMeshSection.meshQuery.LayerQuery}, mask: {subMeshSection.meshQuery.LayerQueryMask})");
                            continue;
                        }

                        // Group by all meshDescriptions with same query
                        if (debugHelpers[helperIndex].Valid)
                        {
                            if (!vertexBufferContents.IsEmpty(i))
                            {
                                Profiler.BeginSample("new ChiselRenderObjectUpdate");
                                var instance = debugHelpers[helperIndex];
                                var meshIndex = foundMeshes.Count;
                                foundMeshes.Add(instance.sharedMesh);
                                renderUpdates.Add(new ChiselRenderObjectUpdate
                                {
                                    contentsIndex = i,
                                    materialOverride = ChiselMaterialManager.HelperMaterials[helperIndex],
                                    instance = instance,
                                    model = model,
                                    meshIndex = meshIndex,
                                    contents = vertexBufferContents
                                });
                                s_GeneratedObjects.Add((this, model));
                                Profiler.EndSample();
                            }
                            helperMeshUpdated[helperIndex] = true;
                        }
                    } else
                    if (subMeshSection.meshQuery.LayerParameterIndex == LayerParameterIndex.RenderMaterial)
                    {
                        var renderIndex = (int)(subMeshSection.meshQuery.LayerQuery & LayerUsageFlags.RenderReceiveCastShadows);
                        if (!vertexBufferContents.IsEmpty(i))
                        {
                            Profiler.BeginSample("new ChiselRenderObjectUpdate");
                            var instance = renderables[renderIndex];
                            var meshIndex = foundMeshes.Count;
                            foundMeshes.Add(instance.sharedMesh);
                            // Group by all meshDescriptions with same query
                            renderUpdates.Add(new ChiselRenderObjectUpdate
                            {
                                contentsIndex = i,
                                materialOverride = null,
                                instance = instance,
                                model = model,
                                meshIndex = meshIndex,
                                contents = vertexBufferContents
                            });
                            s_GeneratedObjects.Add((this, model));
                            Profiler.EndSample();
                        }
                        renderMeshUpdated[renderIndex] = true;
                    } else
                    if (subMeshSection.meshQuery.LayerParameterIndex == LayerParameterIndex.PhysicsMaterial)
                        colliderCount++;
                }
            }
            Profiler.EndSample();







            Profiler.BeginSample("sColliderObjects.Clear");
            if (colliderObjects.Capacity < colliderCount)
                colliderObjects.Capacity = colliderCount;
            for (int i = 0; i < colliderCount; i++)
                colliderObjects.Add(null);
            Profiler.EndSample();

            Profiler.BeginSample("Update.Colliders");
            int colliderIndex = 0;
            if (vertexBufferContents.meshDescriptions.IsCreated)
            {
                for (int i = 0; i < vertexBufferContents.subMeshSections.Length; i++)
                {
                    var subMeshSection = vertexBufferContents.subMeshSections[i];
                    if (subMeshSection.meshQuery.LayerParameterIndex != LayerParameterIndex.PhysicsMaterial)
                        continue;

                    var surfaceParameter = vertexBufferContents.meshDescriptions[subMeshSection.startIndex].surfaceParameter;

                    // TODO: optimize
                    for (int j = 0; j < colliders.Length; j++)
                    {
                        if (colliders[j] == null)
                            continue;
                        if (colliders[j].surfaceParameter != surfaceParameter)
                            continue;

                        colliderObjects[colliderIndex] = colliders[j];
                        colliders[j] = null;
                        break;
                    }

                    Profiler.BeginSample("Create.Colliders");
                    if (colliderObjects[colliderIndex] == null)
                        colliderObjects[colliderIndex] = ChiselColliderObjects.Create(colliderContainer, surfaceParameter);
                    Profiler.EndSample();

                    Profiler.BeginSample("new ChiselPhysicsObjectUpdate");
                    var instance = colliderObjects[colliderIndex];
                    var instanceID = instance.sharedMesh.GetInstanceID();
                    var meshIndex = foundMeshes.Count;
                    foundMeshes.Add(instance.sharedMesh);
                    // Group by all meshDescriptions with same query
                    physicsUpdates.Add(new ChiselPhysicsObjectUpdate
                    {
                        contentsIndex = colliderIndex,
                        instance = instance,
                        meshIndex = meshIndex,
                        instanceID = instanceID,
                        contents = vertexBufferContents
                    });
                    s_GeneratedObjects.Add((this, model));
                    Profiler.EndSample();
                    colliderIndex++;
                }
            }
            Profiler.EndSample();



            // Allocate the number of meshes we need to update 
            //
            // **MAIN THREAD ONLY**
            dataArray = Mesh.AllocateWritableMeshData(foundMeshes.Count);

            // Start jobs to copy mesh data from our generated meshes to unity meshes

            Profiler.BeginSample("Renderers.ScheduleMeshCopy");
            ChiselRenderObjects.ScheduleMeshCopy(renderUpdates, dataArray, ref currentJobHandle, dependencies);
            Profiler.EndSample();

            Profiler.BeginSample("Colliders.ScheduleMeshCopy");
            ChiselColliderObjects.ScheduleMeshCopy(physicsUpdates, dataArray, ref currentJobHandle, dependencies);
            Profiler.EndSample();

            // Start the jobs on the worker threads
            JobHandle.ScheduleBatchedJobs();

            // Now do sll kinds of book-keeping code that we might as well do while our jobs are running on other threads
            Profiler.BeginSample("Colliders.UpdateMaterials");
            ChiselRenderObjects.UpdateMaterials(renderUpdates);
            Profiler.EndSample();

            Profiler.BeginSample("renderables.Clear");
            for (int renderIndex = 0; renderIndex < renderables.Length; renderIndex++)
            {
                if (renderMeshUpdated[renderIndex])
                    continue;
                var renderable = renderables[renderIndex];
                if (!renderable.Valid)
                    continue;
                renderable.Clear(model, gameObjectState);
            }
            Profiler.EndSample();

            Profiler.BeginSample("debugHelpers.Clear");
            for (int helperIndex = 0; helperIndex < debugHelpers.Length; helperIndex++)
            {
                if (helperMeshUpdated[helperIndex])
                    continue;
                var renderable = debugHelpers[helperIndex];
                if (!debugHelpers[helperIndex].Valid)
                    continue;
                renderable.Clear(model, gameObjectState);
            }
            Profiler.EndSample();

            Profiler.BeginSample("CleanUp.Colliders");
            for (int j = 0; j < colliders.Length; j++)
            {
                if (colliders[j] != null)
                    colliders[j].Destroy();
            }
            Profiler.EndSample();

            Profiler.BeginSample("Assign.Colliders");
            if (colliders.Length != colliderCount)
                colliders = new ChiselColliderObjects[colliderCount];
            for (int i = 0; i < colliderCount; i++)
                colliders[i] = colliderObjects[i];
            Profiler.EndSample();

            return currentJobHandle;
        }

        // in between UpdateMeshes and FinishMeshUpdates our jobs should be force completed, so we can now upload our meshes to unity Meshes

        public static void FinishMeshUpdates()
        {
            foreach(var (generatedObject,model) in s_GeneratedObjects)
            {
                Profiler.BeginSample("Apply");
                Mesh.ApplyAndDisposeWritableMeshData(generatedObject.dataArray, generatedObject.foundMeshes, UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
                generatedObject.dataArray = default;
                Profiler.EndSample();

                // TODO: see if we can move this to the end of UpdateMeshes

                Profiler.BeginSample("Renderers.Update");
                ChiselRenderObjects.UpdateSettings(generatedObject.renderUpdates, generatedObject.gameObjectStates);
                Profiler.EndSample();

                Profiler.BeginSample("UpdateProperties");
                ChiselRenderObjects.UpdateProperties(model, generatedObject.meshRenderers);
                Profiler.EndSample();

                Profiler.BeginSample("UpdateColliders");
                ChiselColliderObjects.UpdateProperties(model, generatedObject.colliders);
                Profiler.EndSample();
                generatedObject.needVisibilityMeshUpdate = true;

                generatedObject.gameObjectStates.Clear();
                generatedObject.physicsUpdates.Clear();
                generatedObject.renderUpdates.Clear();
                generatedObject.colliderObjects.Clear();
                generatedObject.foundMeshes.Clear();
                generatedObject.colliderCount = 0;
            }
        }

#if UNITY_EDITOR
        public void RemoveHelperSurfaces()
        {
            for (int i = 0; i < renderables.Length; i++)
            {
                var renderable = renderables[i];
                if (renderable == null ||
                    renderable.invalid ||
                    !renderable.meshRenderer)
                {
                    if (renderable.container)
                        UnityEngine.Object.DestroyImmediate(renderable.container);
                    continue;
                }

                renderable.meshRenderer.forceRenderingOff = false;
            }

            for (int i = 0; i < debugHelpers.Length; i++)
            {
                if (debugHelpers[i].container)
                    UnityEngine.Object.DestroyImmediate(debugHelpers[i].container);
            }
        }

        public void UpdateHelperSurfaceState(DrawModeFlags helperStateFlags, bool ignoreBrushVisibility = false)
        {
            if (!ignoreBrushVisibility)
                ChiselGeneratedComponentManager.UpdateVisibility();
            
            var shouldHideMesh  = !ignoreBrushVisibility &&
                                  visibilityState != VisibilityState.AllVisible &&
                                  visibilityState != VisibilityState.Unknown;
                                  
            var showRenderables = (helperStateFlags & DrawModeFlags.HideRenderables) == DrawModeFlags.None;
            for (int i = 0; i < renderables.Length; i++)
            {
                var renderable = renderables[i];
                if (renderable == null ||
                    renderable.invalid)
                    continue;

                if (renderable.meshRenderer != null)
                    renderable.meshRenderer.forceRenderingOff = shouldHideMesh || !showRenderables;
            }

            for (int i = 0; i < debugHelpers.Length; i++)
            {
                var showState    = (helperStateFlags & kGeneratedDebugShowFlags[i]) != DrawModeFlags.None;
                if (debugHelpers[i].meshRenderer != null)
                    debugHelpers[i].meshRenderer.forceRenderingOff = shouldHideMesh || !showState;
            }

            if (ignoreBrushVisibility || !needVisibilityMeshUpdate)
                return;

            if (visibilityState == VisibilityState.Mixed)
            {
                for (int i = 0; i < renderables.Length; i++)
                {
                    var renderable = renderables[i];
                    if (renderable == null ||
                        renderable.invalid)
                        continue;

                    renderable.UpdateVisibilityMesh(showRenderables);
                }

                for (int i = 0; i < debugHelpers.Length; i++)
                {
                    var show = (helperStateFlags & kGeneratedDebugShowFlags[i]) != DrawModeFlags.None;
                    var debugHelper = debugHelpers[i];
                    if (debugHelper == null)
                        continue;

                    debugHelper.UpdateVisibilityMesh(show);
                }
            }

            needVisibilityMeshUpdate = false;
        }
#endif
    }
}