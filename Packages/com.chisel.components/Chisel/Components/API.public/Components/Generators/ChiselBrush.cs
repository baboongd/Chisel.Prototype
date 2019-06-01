﻿using UnityEngine;
using Chisel.Core;

namespace Chisel.Components
{
    [ExecuteInEditMode]
    [HelpURL(kDocumentationBaseURL + kNodeTypeName + kDocumentationExtension)]
    [AddComponentMenu("Chisel/" + kNodeTypeName)]
    public sealed class ChiselBrush : ChiselGeneratorComponent
    {
        public const string kNodeTypeName = "Brush";
        public override string NodeTypeName { get { return kNodeTypeName; } }

        public override void UpdateGenerator()
        {
            // This class doesn't generate anything, so we keep the original brush
        }

        protected override void UpdateGeneratorInternal() { }
    }
}