﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using VRage.Collections;
using VRageMath;

namespace Sandbox.Game.AI.Pathfinding
{
    /// <summary>
    /// A helper class to store mapping from triangles (represented by their int index)
    /// to components (represented by their int index) and cells (ulong), given following conditions:
    /// 
    /// a) Each triangle belongs to a cell, which is assigned a ulong coordinate, and to a component
    /// b) A component cannot span more cells - that is, if two triangles are in the same component, they must also be in the same cell
    /// </summary>
    public class MyNavmeshComponents
    {
        public struct CellInfo
        {
            public int StartingIndex;
            public ushort ComponentNum;
            public Base6Directions.DirectionFlags ExploredDirections;

            public override string ToString()
            {
                return ComponentNum.ToString() + " components: " + StartingIndex + " - " + (StartingIndex + ComponentNum - 1) + ", Expl.: " + ExploredDirections.ToString();
            }
        }

        public struct ClosedCellInfo
        {
            public bool NewCell;
            public int StartingIndex;
            public ushort ComponentNum;
            public Base6Directions.DirectionFlags ExploredDirections; // CH:TODO: Maybe get rid of this. It's duplicated in MyVoxelHighLevelHelper.m_exploredCells
        }

        private Dictionary<ulong, CellInfo> m_cellInfos;

        // Maps component numbers to their containing cells
        private Dictionary<int, ulong> m_componentCells;

        private int m_nextComponentIndex;
        private int m_currentStartingComponent;

        private bool m_cellOpen;
        private bool m_componentOpen;

        // Is reset when a new cell is open
        private List<Vector3> m_lastCellComponentCenters;

        // Only valid while a cell is open:
        private ulong m_cellCoord;
        private ushort m_componentNum;

        // Only valid while a component is open:
        private List<MyIntervalList> m_components;

        public int OpenComponentIndex
        {
            get
            {
                Debug.Assert(m_componentOpen && m_cellOpen);
                return m_currentStartingComponent + m_componentNum;
            }
        }

        public MyNavmeshComponents()
        {
            m_cellOpen = false;
            m_componentOpen = false;

            m_cellInfos = new Dictionary<ulong, CellInfo>();
            m_componentCells = new Dictionary<int, ulong>();
            m_currentStartingComponent = 0;
            m_nextComponentIndex = 0;

            m_components = null;
            m_lastCellComponentCenters = new List<Vector3>();
        }

        public int OpenCell(ulong cellCoord)
        {
            Debug.Assert(!m_cellOpen, "Opening a new cell while the previous one is still open in TriangleComponentMapping!");

            MyNavmeshComponents.CellInfo existingCell;
            if (TryGetCell(cellCoord, out existingCell))
            {
                m_currentStartingComponent = existingCell.StartingIndex;
            }
            else
            {
                m_currentStartingComponent = m_nextComponentIndex;
            }

            m_cellOpen = true;
            m_cellCoord = cellCoord;
            m_componentNum = 0;
            m_components = new List<MyIntervalList>();
            m_lastCellComponentCenters.Clear();

            return m_currentStartingComponent;
        }

        public void CloseAndCacheCell(ref ClosedCellInfo output)
        {
            Debug.Assert(m_cellOpen, "Closing a cell in TriangleComponentMapping, but no cell is open!");

            CellInfo info = new CellInfo();
            if (m_cellInfos.TryGetValue(m_cellCoord, out info))
            {
                // CH: TODO: Check this assert.
                Debug.Assert(m_componentNum == info.ComponentNum);

                output.NewCell = false;
            }
            else
            {
                info.ComponentNum = m_componentNum;
                info.StartingIndex = m_currentStartingComponent;

                if (m_currentStartingComponent + m_componentNum > m_nextComponentIndex)
                {
                    m_nextComponentIndex = m_currentStartingComponent + m_componentNum;
                }

                output.NewCell = true;
            }

            // Save information about containing cell
            for (int i = 0; i < info.ComponentNum; ++i)
            {
                m_componentCells[info.StartingIndex + i] = m_cellCoord;
            }

            m_cellInfos[m_cellCoord] = info;

            output.ComponentNum = info.ComponentNum;
            output.ExploredDirections = info.ExploredDirections;
            output.StartingIndex = info.StartingIndex;

            m_components = null;
            m_componentNum = 0;
            m_cellOpen = false;
        }

        public void OpenComponent()
        {
            Debug.Assert(!m_componentOpen, "Opening a new component while the previous one is still open in TriangleComponentMapping!");
            m_componentOpen = true;

            m_lastCellComponentCenters.Add(Vector3.Zero);
            m_components.Add(new MyIntervalList());
        }

        public void AddComponentTriangle(MyNavigationTriangle triangle, Vector3 center)
        {
            Debug.Assert(m_componentOpen, "Adding a triangle to a component in TriangleComponentMapping, when no component is open!");

            int triIndex = triangle.Index;

            MyIntervalList triList = m_components[m_componentNum];
            triList.Add(triIndex);

            float t = 1.0f / triList.Count;
            m_lastCellComponentCenters[m_componentNum] = center * t + m_lastCellComponentCenters[m_componentNum] * (1.0f - t);
        }

        public void CloseComponent()
        {
            Debug.Assert(m_cellOpen, "Closing a component in TriangleComponentMapping, but no component is open!");

            m_componentNum++;
            m_componentOpen = false;
        }

        public Vector3 GetComponentCenter(int index)
        {
            return m_lastCellComponentCenters[index];
        }

        public bool TryGetComponentCell(int componentIndex, out ulong cellIndex)
        {
            return m_componentCells.TryGetValue(componentIndex, out cellIndex);
        }

        public bool GetComponentCell(int componentIndex, out ulong cellIndex)
        {
            Debug.Assert(m_componentCells.ContainsKey(componentIndex), "Could not get component cell!");
            return m_componentCells.TryGetValue(componentIndex, out cellIndex);
        }

        public bool GetComponentInfo(int componentIndex, ulong cellIndex, out Base6Directions.DirectionFlags exploredDirections)
        {
            exploredDirections = (Base6Directions.DirectionFlags)0;

            CellInfo cellInfo;
            bool success = TryGetCell(cellIndex, out cellInfo);
            Debug.Assert(success, "Could not retrieve cell info for a cell!");

            int relativeComponentIndex = componentIndex - cellInfo.StartingIndex;
            Debug.Assert(relativeComponentIndex >= 0 && relativeComponentIndex < cellInfo.ComponentNum, "Component index overflow! The component does not belong to this cell!");
            if (relativeComponentIndex < 0 || relativeComponentIndex >= cellInfo.ComponentNum)
            {
                return false;
            }

            exploredDirections = cellInfo.ExploredDirections;
            return true;
        }

        public void MarkExplored(ulong otherCell, Base6Directions.Direction direction)
        {
            CellInfo info = new CellInfo();
            if (m_cellInfos.TryGetValue(otherCell, out info))
            {
                info.ExploredDirections |= Base6Directions.GetDirectionFlag(direction);
                m_cellInfos[otherCell] = info;
            }
            else
            {
                Debug.Assert(false, "Inconsistency: Cannot find cell info to mark explored information!");
            }
        }

        public void SetExplored(ulong packedCoord, Base6Directions.DirectionFlags directionFlags)
        {
            CellInfo info = new CellInfo();
            if (m_cellInfos.TryGetValue(packedCoord, out info))
            {
                info.ExploredDirections = directionFlags;
                m_cellInfos[packedCoord] = info;
            }
            else
            {
                Debug.Assert(false, "Could not find navmesh cell info for setting explored directions!");
            }
        }

        public bool TryGetCell(ulong packedCoord, out CellInfo cellInfo)
        {
            return m_cellInfos.TryGetValue(packedCoord, out cellInfo);
        }

        public ICollection<ulong> GetPresentCells()
        {
            return m_cellInfos.Keys;
        }

        public void ClearCell(ulong packedCoord, ref CellInfo cellInfo)
        {
            Debug.Assert(m_cellInfos.ContainsKey(packedCoord), "Could not find navmesh cell info for clearing!");

            // Remove information about containing cell
            for (int i = 0; i < cellInfo.ComponentNum; ++i)
            {
                bool success = m_componentCells.Remove(cellInfo.StartingIndex + i);
                Debug.Assert(success, "Inconsistency! Couldn't remove information about cell of a cached navmesh component cell");
            }

            m_cellInfos.Remove(packedCoord);
        }
    }
}
