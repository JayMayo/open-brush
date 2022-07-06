﻿// Copyright 2022 Chingiz Dadashov-Khandan
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using UnityEngine;

namespace TiltBrush {
public abstract class BaseSculptSubTool : MonoBehaviour {

    protected float m_DefaultStrength = 0.1f;

    public SculptSubToolManager.SubTool m_SubToolIdentifier;

    protected Collider m_Collider;

    

    abstract public Vector3 ManipulateVertex(Vector3 vertex, bool bPushing, TrTransform canvasPose, Transform toolTransform, float toolSize, BatchSubset rGroup);
    
    /// For sculpting tools with an interactor that limits the sculpting tool's
    /// sphere of influence. If the interactor doesn't exist or shouldn't limit things, this is ignored.
    virtual protected bool IsInReach(Vector3 vertex) {
        return true;
    }

    // virtual public float CalculateStrength(Vector3 vertex, float distance, TrTransform canvasPose,  bool bPushing) {
    //     return m_DefaultStrength;
    // }

    // abstract public Vector3 CalculateDirection(Vector3 vertex, Transform toolTransform, TrTransform canvasPose, bool bPushing, BatchSubset rGroup);
}
} //namespace TiltBrush
