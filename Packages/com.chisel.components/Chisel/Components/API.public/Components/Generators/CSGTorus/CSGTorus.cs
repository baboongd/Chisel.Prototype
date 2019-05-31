﻿using UnityEngine;
using System.Collections;
using Chisel.Core;
using System.Collections.Generic;
using System;

namespace Chisel.Components
{
    [ExecuteInEditMode]
    [HelpURL(ChiselGeneratorComponent.kDocumentationBaseURL + nameof(CSGTorus) + ChiselGeneratorComponent.KDocumentationExtension)]
    [AddComponentMenu("Chisel/" + CSGTorus.kNodeTypeName)]
    public sealed class CSGTorus : ChiselGeneratorComponent
    {
        public const string kNodeTypeName = "Torus";
        public override string NodeTypeName { get { return kNodeTypeName; } }

        // TODO: make this private
        [SerializeField] public CSGTorusDefinition definition = new CSGTorusDefinition();

        // TODO: implement properties

        protected override void OnValidateInternal() { definition.Validate(); base.OnValidateInternal(); }
        protected override void OnResetInternal()	 { definition.Reset(); base.OnResetInternal(); }

        protected override void UpdateGeneratorInternal()
        {
            var brushMeshes = generatedBrushes.BrushMeshes;
            if (!BrushMeshFactory.GenerateTorus(ref brushMeshes, ref definition))
            {
                generatedBrushes.Clear();
                return;
            }

            generatedBrushes.SetSubMeshes(brushMeshes);
            generatedBrushes.CalculatePlanes();
            generatedBrushes.SetDirty();
        }
    }
}
