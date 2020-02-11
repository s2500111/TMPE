namespace TrafficManager.Util {
    using System;
    using System.Collections.Generic;
    using API.Traffic.Data;
    using API.Traffic.Enums;
    using Manager.Impl;
    using GenericGameBridge.Service;
    using CSUtil.Commons;
    using UnityEngine;
    using static TrafficManager.Util.SegmentTraverser;
    using State;
    using static TrafficManager.Util.Shortcuts;
    using static TrafficManager.UI.SubTools.PrioritySignsTool;
    using TrafficManager.API.Manager;

    public static class PriorityRoad {
        public static void FixPrioritySigns(PrioritySignsMassEditMode massEditMode, List<ushort> segmentList) {
            if (segmentList == null || segmentList.Count == 0) {
                return;
            }

            var primaryPrioType = PriorityType.None;
            var secondaryPrioType = PriorityType.None;

            switch (massEditMode) {
                case PrioritySignsMassEditMode.MainYield: {
                        primaryPrioType = PriorityType.Main;
                        secondaryPrioType = PriorityType.Yield;
                        break;
                    }

                case PrioritySignsMassEditMode.MainStop: {
                        primaryPrioType = PriorityType.Main;
                        secondaryPrioType = PriorityType.Stop;
                        break;
                    }

                case PrioritySignsMassEditMode.YieldMain: {
                        primaryPrioType = PriorityType.Yield;
                        secondaryPrioType = PriorityType.Main;
                        break;
                    }

                case PrioritySignsMassEditMode.StopMain: {
                        primaryPrioType = PriorityType.Stop;
                        secondaryPrioType = PriorityType.Main;
                        break;
                    }
            }

            IExtSegmentEndManager segEndMan = Constants.ManagerFactory.ExtSegmentEndManager;

            void ApplyPrioritySigns(ushort segmentId, bool startNode) {
                ushort nodeId = netService.GetSegmentNodeId(
                    segmentId,
                    startNode);

                TrafficPriorityManager.Instance.SetPrioritySign(
                    segmentId,
                    startNode,
                    primaryPrioType);

                for (int i = 0; i < 8; ++i) {
                    ushort otherSegmentId = nodeId.ToNode().GetSegment(i);
                    if (otherSegmentId == 0 ||
                        otherSegmentId == segmentId ||
                        segmentList.Contains(otherSegmentId)) {
                        continue;
                    }

                    TrafficPriorityManager.Instance.SetPrioritySign(
                        otherSegmentId,
                        (bool)netService.IsStartNode(otherSegmentId, nodeId),
                        secondaryPrioType);
                }
            }

            foreach(ushort segId in segmentList) {
                foreach(bool bStartNode in Constants.ALL_BOOL) {
                    ApplyPrioritySigns(segId, bStartNode);
                }
            }
        }


        private static void Swap(this List<ushort> list, int i1, int i2) {
            ushort temp = list[i1];
            list[i1] = list[i2];
            list[i2] = temp;
        }

        private static LaneArrows ToLaneArrows(ArrowDirection dir) {
            switch (dir) {
                case ArrowDirection.Forward:
                    return LaneArrows.Forward;
                case ArrowDirection.Left:
                    return LaneArrows.Left;
                case ArrowDirection.Right:
                    return LaneArrows.Right;
                default:
                    return LaneArrows.None;
            }
        }

        /// <summary>
        /// Quick-setups as priority junction: for every junctions on the road contianing
        /// the input segment traversing straight.
        /// </summary>
        public static void FixRoad(ushort initialSegmentId) {
            SegmentTraverser.Traverse(
                initialSegmentId,
                TraverseDirection.AnyDirection,
                TraverseSide.Straight,
                SegmentStopCriterion.None,
                VisitorFunc);
        }

        private static bool VisitorFunc(SegmentVisitData data) {
            ushort segmentId = data.CurSeg.segmentId;
            foreach (bool startNode in Constants.ALL_BOOL) {
                ushort nodeId = netService.GetSegmentNodeId(segmentId, startNode);
                FixHighPrioirtyJunction(nodeId);
            }
            return true;
        }

        /// <summary>
        /// returns the node of <paramref name="segmentId"/> that is not shared 
        /// with <paramref name="otherSegmentId"/> .
        /// </summary>
        /// <param name="segmentId"></param>
        /// <param name="otherSegmentId"></param>
        /// <returns></returns>
        private static ushort GetUnsharedNode(ushort segmentId, ushort otherSegmentId) {
            ref NetSegment segment = ref segmentId.ToSegment();
            ushort sharedNodeId = segment.GetSharedNode(otherSegmentId);
            if (sharedNodeId == 0)
                return 0;
            return segment.GetOtherNode(sharedNodeId);
        }

        /// <summary>
        /// Quick-setups as priority junction: for every junctions on the road contianing
        /// the input segment traversing straight.
        /// </summary>
        public static void FixRoad(List<ushort> segmentList) {
            ushort firstNodeId = GetUnsharedNode(segmentList[0], segmentList[1]);
            int last = segmentList.Count - 1;
            ushort lastNodeId = GetUnsharedNode(segmentList[last], segmentList[last-1]);
            if(firstNodeId == lastNodeId) {
                firstNodeId = lastNodeId = 0;
            }

            foreach (ushort segmentId in segmentList) {
                foreach (bool startNode in Constants.ALL_BOOL) {
                    ushort nodeId = netService.GetSegmentNodeId(segmentId, startNode);
                    bool isEndNode = nodeId == firstNodeId || nodeId == lastNodeId;
                    if (isEndNode) {
                        FixHighPrioirtyJunction(nodeId);
                    } else {
                        FixHighPrioirtyJunction(nodeId, segmentList);
                    }
                }
            }
        }

        private static bool IsStraighOneWay(ushort segmentId0, ushort segmentId1) {
            ref NetSegment seg0 = ref GetSeg(segmentId0);
            //ref NetSegment seg1 = ref GetSeg(segmentId1);
            bool oneway = segMan.CalculateIsOneWay(segmentId0) &&
                          segMan.CalculateIsOneWay(segmentId1);
            if (!oneway) {
                return false;
            }

            ushort nodeId;
            if ((nodeId = netService.GetHeadNode(segmentId0)) == netService.GetTailNode(segmentId1)) {
                if (GetDirection(segmentId0, segmentId1, nodeId) == ArrowDirection.Forward) {
                    return true;
                }
            } else if ((nodeId = netService.GetHeadNode(segmentId1)) == netService.GetTailNode(segmentId0)) {
                if (GetDirection(segmentId1, segmentId0, nodeId) == ArrowDirection.Forward) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// if the roads form a T shape then try to arrange them like this (if possible)
        /// slot 0: incomming oneway road.
        /// slot 1: outgoing oneway road.
        /// slot 2: 2 way raod.
        /// Post Condtion: the arrangement of <paramref name="segmentList"/> might be
        /// altered regardless of return value.
        /// </summary>
        /// <returns>true on sucessful arrangement, false otherwise</returns>
        private static bool ArrangeT(List<ushort> segmentList) {
            if (segmentList.Count != 3) {
                return false;
            }
            bool oneway0 = segMan.CalculateIsOneWay(segmentList[0]);
            bool oneway1 = segMan.CalculateIsOneWay(segmentList[1]);
            bool oneway2 = segMan.CalculateIsOneWay(segmentList[2]);
            int sum = Int(oneway0) + Int(oneway1) + Int(oneway2); // number of one way roads

            // put the two-way road in slot 2.
            if (sum != 2) {
                // expected a one way road and 2 two-way roads.
                return false;
            } else if (!oneway0) {
                segmentList.Swap( 0, 2);
            } else if (!oneway1) {
                segmentList.Swap( 1, 2);
            }

            // slot 0: incomming road.
            // slot 1: outgoing road.
            if (netService.GetHeadNode(segmentList[1]) == netService.GetTailNode(segmentList[0])) {
                segmentList.Swap( 0, 1);
                return true;
            }

            return netService.GetHeadNode(segmentList[0]) == netService.GetTailNode(segmentList[1]);
        }

        private static void HandleSplitAvenue(List<ushort> segmentList, ushort nodeId) {
            Log._Debug($"HandleSplitAvenue(segmentList, {nodeId}) was called");
            void SetArrows(ushort segmentIdSrc, ushort segmentIdDst) {
                LaneArrows arrow = ToLaneArrows(GetDirection(segmentIdSrc, segmentIdDst, nodeId));
                IList<LanePos> lanes = netService.GetSortedLanes(
                                segmentIdSrc,
                                ref GetSeg(segmentIdSrc),
                                netService.IsStartNode(segmentIdSrc, nodeId),
                                LaneArrowManager.LANE_TYPES,
                                LaneArrowManager.VEHICLE_TYPES,
                                true);

                foreach (LanePos lane in lanes) {
                    LaneArrowManager.Instance.SetLaneArrows(lane.laneId, arrow, true);
                }
            }

            SetArrows(segmentList[0], segmentList[2]);
            SetArrows(segmentList[2], segmentList[1]);
            foreach(ushort segmentId in segmentList) {
                FixMajorSegmentRules(segmentId, nodeId);
            }
        }

        /// <summary>
        /// Quick-setups the given junction as priority junction.
        /// The roads on the segmentList are considered as main road.
        /// All other roads are considered as minor road.
        /// Also detects:
        ///  - semi-roundabout
        ///  - split avenue into 2 oneway roads (only applicable to first or last node).
        /// </summary>
        public static void FixHighPrioirtyJunction(ushort nodeId, List<ushort> segmentList) {
            if (nodeId == 0) {
                return;
            }

            List<ushort> nodeSegments = new List<ushort>();
            for (int i = 0; i < 8; ++i) {
                ushort segId = nodeId.ToNode().GetSegment(i);
                if (segId != 0) {
                    bool main = segmentList.Contains(segId);
                    if (main) {
                        nodeSegments.Insert(0, segId);
                    } else {
                        nodeSegments.Add(segId);
                    }
                }
            }

            if (nodeSegments.Count < 3) {
                Log._Debug("FixJunction: This is not a junction.");
                return;
            }

            FixHighPrioirtyJunctionHelper(nodeId, nodeSegments);
        } // end method

        /// <summary>
        /// Quick-setups the given junction as priority junction.
        /// The two biggest roads are considererd prioirty road.
        /// all other roads are considered minor road.
        /// Also detects:
        ///  - split avenue into 2 oneway roads
        ///  - semi-roundabout
        /// </summary>
        public static void FixHighPrioirtyJunction(ushort nodeId) {
            if (nodeId == 0) {
                return;
            }

            List<ushort> nodeSegments = new List<ushort>();
            for (int i = 0; i < 8; ++i) {
                ushort segId = nodeId.ToNode().GetSegment(i);
                if (segId != 0) {
                    nodeSegments.Add(segId);
                }
            }

            if (nodeSegments.Count < 3) {
                Log._Debug("FixJunction: This is not a junction.");
                return;
            }

            if (ArrangeT(nodeSegments)) {
                bool isSemiRabout = GetDirection(nodeSegments[0], nodeSegments[1], nodeId) == ArrowDirection.Forward;
                // isSemiRabout if one of these shapes: |- \- /-
                // split avenue if one of these shapes: >-  <-
                // they are all T shaped the difference is the angle.
                if (!isSemiRabout) {
                    HandleSplitAvenue(nodeSegments, nodeId);
                    return;
                }
            } else {
                nodeSegments.Sort(CompareSegments);
            }

            if (CompareSegments(nodeSegments[1], nodeSegments[2]) == 0) {
                Log._Debug("FixJunction: cannot determine which road should be treaded as the main road.\n" +
                    "segmentList=" + nodeSegments.ToSTR());
                return;
            }

            FixHighPrioirtyJunctionHelper(nodeId, nodeSegments);
        }

        /// <summary>
        /// apply high priority junction rules 
        /// - supports semi-roundabout.
        /// - no support for road spliting.
        /// </summary>
        /// <param name="nodeId">Junction to apply rules</param>
        /// <param name="nodeSegments">list of segments. The first two elements are main/roundabout,
        /// all other semenets are minor</param>
        private static void FixHighPrioirtyJunctionHelper(ushort nodeId, List<ushort> nodeSegments) {
            bool isSemiRabout =
                nodeSegments.Count == 3 &&
                IsStraighOneWay(nodeSegments[0], nodeSegments[1]);

            // "far turn" is allowed when the main road is oneway.
            bool ignoreLanes =
                segMan.CalculateIsOneWay(nodeSegments[0]) ||
                segMan.CalculateIsOneWay(nodeSegments[1]);

            // Turning allowed when the main road is agnled.
            ArrowDirection dir = GetDirection(nodeSegments[0], nodeSegments[1], nodeId);
            ignoreLanes &= dir != ArrowDirection.Forward;
            ignoreLanes |= OptionsMassEditTab.PriorityRoad_AllowLeftTurns;

            Log._Debug($"ignorelanes={ignoreLanes} isSemiRabout={isSemiRabout}\n" +
                        "segmentList=" + nodeSegments.ToSTR());

            for (int i = 0; i < nodeSegments.Count; ++i) {
                ushort segmentId = nodeSegments[i];
                if (i < 2) {
                    if (isSemiRabout) {
                        RoundaboutMassEdit.FixRulesRAbout(segmentId);
                    } else {
                        FixMajorSegmentRules(segmentId, nodeId);
                    }
                    if(!ignoreLanes) {
                        FixMajorSegmentLanes(segmentId, nodeId);
                    }
                } else {
                    if (isSemiRabout) {
                        RoundaboutMassEdit.FixRulesMinor(segmentId, nodeId);
                    } else {
                        FixMinorSegmentRules(segmentId, nodeId, nodeSegments);
                    }
                    if (!ignoreLanes) {
                        FixMinorSegmentLanes(segmentId, nodeId, nodeSegments);
                    }
                }
            } //end for
        } // end method

        private static ArrowDirection GetDirection(ushort segmentId, ushort otherSegmentId, ushort nodeId) {
            ref ExtSegmentEnd segEnd = ref GetSegEnd(segmentId, nodeId);
            ArrowDirection dir = segEndMan.GetDirection(ref segEnd, otherSegmentId);
            return dir;
        }

        private static void FixMajorSegmentRules(ushort segmentId, ushort nodeId) {
            Log._Debug($"FixMajorSegmentRules({segmentId}, {nodeId}) was called");
            bool startNode = (bool)netService.IsStartNode(segmentId, nodeId);
            JunctionRestrictionsManager.Instance.SetEnteringBlockedJunctionAllowed(segmentId, startNode, true);
            if(!OptionsMassEditTab.PriorityRoad_CrossMainR) {
                JunctionRestrictionsManager.Instance.SetPedestrianCrossingAllowed(segmentId, startNode, false);
            }
            TrafficPriorityManager.Instance.SetPrioritySign(segmentId, startNode, PriorityType.Main);
        }


        private static void FixMinorSegmentRules(ushort segmentId, ushort nodeId, List<ushort> segmentList) {
            Log._Debug($"FixMinorSegmentRules({segmentId}, {nodeId}, segmentList) was called");
            bool startNode = (bool)netService.IsStartNode(segmentId, nodeId);
            if (OptionsMassEditTab.PriorityRoad_EnterBlockedYeild) {
                JunctionRestrictionsManager.Instance.SetEnteringBlockedJunctionAllowed(segmentId, startNode, true);
            }
            if (HasAccelerationLane(segmentList, segmentId, nodeId)) {
                JunctionRestrictionsManager.Instance.SetEnteringBlockedJunctionAllowed(segmentId, startNode, true);
            } else if (OptionsMassEditTab.PriorityRoad_StopAtEntry) {
                TrafficPriorityManager.Instance.SetPrioritySign(segmentId, startNode, PriorityType.Stop);
            } else {
                TrafficPriorityManager.Instance.SetPrioritySign(segmentId, startNode, PriorityType.Yield);
            }
        }

        private static int CountLanes(ushort segmentId, ushort nodeId, bool toward) {
            return netService.GetSortedLanes(
                                segmentId,
                                ref segmentId.ToSegment(),
                                netService.IsStartNode(segmentId, nodeId) ^ (!toward),
                                LaneArrowManager.LANE_TYPES,
                                LaneArrowManager.VEHICLE_TYPES,
                                true
                                ).Count;
        }
        private static int CountLanesTowardJunction(ushort segmentId, ushort nodeId) => CountLanes(segmentId, nodeId, true);
        private static int CountLanesAgainstJunction(ushort segmentId, ushort nodeId) => CountLanes(segmentId, nodeId, false);


        private static bool HasAccelerationLane(List<ushort> segmentList, ushort segmentId, ushort nodeId) {
            bool lht = LaneArrowManager.Instance.Services.SimulationService.TrafficDrivesOnLeft;
            if (!segMan.CalculateIsOneWay(segmentId)) {
                return false;
            }
            bool IsMain(ushort segId) {
                return segId == segmentList[0] || segId == segmentList[1];
            }
            ref NetSegment seg = ref segmentId.ToSegment();

            ushort MainAgainst, MainToward;
            if (lht) {
                MainAgainst = seg.GetLeftSegment(nodeId);
                MainToward = seg.GetRightSegment(nodeId);
            } else {
                MainAgainst = seg.GetRightSegment(nodeId);
                MainToward = seg.GetLeftSegment(nodeId);
            }

            Log._Debug($"HasAccelerationLane: segmentId:{segmentId} MainToward={MainToward} MainAgainst={MainAgainst} ");
            if (IsMain(MainToward) && IsMain(MainAgainst) ) {
                int Yt = CountLanesTowardJunction(segmentId, nodeId); // Yeild Toward.
                int Mt = CountLanesTowardJunction(MainToward, nodeId); // Main Toward.
                int Ma = CountLanesAgainstJunction(MainAgainst, nodeId); // Main Against.
                bool ret = Yt > 0 && Yt + Mt <= Ma;
                Log._Debug($"HasAccelerationLane: Yt={Yt}  Mt={Mt} Ma={Ma} ret={ret} : Yt + Mt <= Ma ");
                return ret;
            }

            return false;
        }

        private static void FixMajorSegmentLanes(ushort segmentId, ushort nodeId) {
            Log._Debug($"FixMajorSegmentLanes({segmentId}, {nodeId}) was called");
            if (LaneArrowManager.SeparateTurningLanes.CanChangeLanes(segmentId, nodeId) != SetLaneArrowError.Success) {
                Log._Debug("FixMajorSegmentLanes: can't change lanes");
                return;
            }

            ref NetSegment seg = ref segmentId.ToSegment();
            ref NetNode node = ref nodeId.ToNode();
            bool startNode = (bool)netService.IsStartNode(segmentId, nodeId);
            bool lht = LaneArrowManager.Instance.Services.SimulationService.TrafficDrivesOnLeft;

            //list of outgoing lanes from current segment to current node.
            IList<LanePos> laneList =
                netService.GetSortedLanes(
                    segmentId,
                    ref seg,
                    startNode,
                    LaneArrowManager.LANE_TYPES,
                    LaneArrowManager.VEHICLE_TYPES,
                    !lht
                    );
            int srcLaneCount = laneList.Count;
            Log._Debug($"FixMajorSegmentLanes: segment:{segmentId} laneList:" + laneList.ToSTR());

            bool bLeft, bRight, bForward;
            ref ExtSegmentEnd segEnd = ref GetSegEnd(segmentId, nodeId);
            segEndMan.CalculateOutgoingLeftStraightRightSegments(ref segEnd, ref node, out bLeft, out bForward, out bRight);

            LaneArrows arrowShort = lht ? LaneArrows.Left : LaneArrows.Right;
            LaneArrows arrowFar = lht ? LaneArrows.Right : LaneArrows.Left;
            for (int i = 0; i < srcLaneCount; ++i) {
                uint laneId = laneList[i].laneId;
                LaneArrows arrows = LaneArrowManager.Instance.GetFinalLaneArrows(laneId);
                LaneArrowManager.Instance.RemoveLaneArrows(
                    laneId,
                    arrowFar);

                if (arrows != arrowShort) {
                    LaneArrowManager.Instance.SetLaneArrows(
                        laneList[i].laneId,
                        LaneArrows.Forward);
                }
            }

            bool bShort = lht ? bLeft : bRight;
            if (srcLaneCount > 0 && bShort) {
                LanePos outerMostLane = laneList[laneList.Count - 1];
                LaneArrowManager.Instance.AddLaneArrows(outerMostLane.laneId, arrowShort);
            }

        }

        private static void FixMinorSegmentLanes(ushort segmentId, ushort nodeId, List<ushort> segmentList) {
            Log._Debug($"FixMinorSegmentLanes({segmentId}, {nodeId}, segmentList) was called");
            if (LaneArrowManager.SeparateTurningLanes.CanChangeLanes(segmentId, nodeId) != SetLaneArrowError.Success) {
                Debug.Log("FixMinorSegmentLanes(): can't change lanes");
                return;
            }
            ref NetSegment seg = ref segmentId.ToSegment();
            ref NetNode node = ref nodeId.ToNode();
            bool startNode = (bool)netService.IsStartNode(segmentId, nodeId);

            //list of outgoing lanes from current segment to current node.
            IList<LanePos> laneList =
                netService.GetSortedLanes(
                    segmentId,
                    ref seg,
                    startNode,
                    LaneArrowManager.LANE_TYPES,
                    LaneArrowManager.VEHICLE_TYPES,
                    true
                    );
            int srcLaneCount = laneList.Count;

            bool bLeft, bRight, bForward;
            ref ExtSegmentEnd segEnd = ref GetSegEnd(segmentId, nodeId);
            segEndMan.CalculateOutgoingLeftStraightRightSegments(ref segEnd, ref node, out bLeft, out bForward, out bRight);

            // LHD vs RHD variables.
            bool lht = LaneArrowManager.Instance.Services.SimulationService.TrafficDrivesOnLeft;
            ArrowDirection nearDir = lht ? ArrowDirection.Left : ArrowDirection.Right;
            LaneArrows nearArrow   = lht ? LaneArrows.Left     : LaneArrows.Right;
            bool             bnear = lht ? bLeft               : bRight;
            int sideLaneIndex      = lht ? srcLaneCount - 1    : 0;

            LaneArrows turnArrow = nearArrow;
            {
                // Check for slight turn into the main road.
                ArrowDirection dir0 = segEndMan.GetDirection(ref segEnd, segmentList[0]);
                ArrowDirection dir1 = segEndMan.GetDirection(ref segEnd, segmentList[1]);
                Debug.Assert(dir1 != dir0); // Assume main road is not angled: then dir1 != dir0
                if (dir0 != nearDir && dir1 != nearDir) {
                    turnArrow = LaneArrows.Forward; //slight turn uses forward arrow.
                }
            }

            // only take the near turn into main road.
            for (int i = 0; i < srcLaneCount; ++i) {
                LaneArrowManager.Instance.SetLaneArrows(laneList[i].laneId, turnArrow);
            }

            /* in case there are multiple minor roads attached to the priority road at the same side
             * and the main road is straigh, then add a turn arrow into the other minor roads.
             */
            if(srcLaneCount > 0 && bnear && turnArrow == LaneArrows.Forward) {
                LaneArrowManager.Instance.AddLaneArrows( 
                    laneList[sideLaneIndex].laneId,
                    nearArrow);
            }
        }

        private static int CompareSegments(ushort seg1Id, ushort seg2Id) {
            ref NetSegment seg1 = ref GetSeg(seg1Id);
            ref NetSegment seg2 = ref GetSeg(seg2Id);
            int diff = (int)Math.Ceiling(seg2.Info.m_halfWidth - seg1.Info.m_halfWidth);
            if (diff == 0) {
                diff = CountRoadVehicleLanes(seg2Id) - CountRoadVehicleLanes(seg1Id);
            }
            return diff;
        }

        private static int CountRoadVehicleLanes(ushort segmentId) {
            ref NetSegment segment = ref segmentId.ToSegment();
            int forward = 0, backward = 0;
            segment.CountLanes(
                segmentId,
                        NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle,
                        VehicleInfo.VehicleType.Car,
                        ref forward,
                        ref backward);
            return forward + backward;
        }

        public static void ClearNode(ushort nodeId) {
            LaneConnectionManager.Instance.RemoveLaneConnectionsFromNode(nodeId);
            netService.IterateNodeSegments(nodeId, (ushort segmentId, ref NetSegment seg) => {
                ref NetNode node = ref GetNode(nodeId);
                bool startNode = (bool)netService.IsStartNode(segmentId, nodeId);
                TPMan.SetPrioritySign(segmentId, startNode, PriorityType.None);
                JPMan.SetPedestrianCrossingAllowed(segmentId, startNode, TernaryBool.Undefined);
                JPMan.SetEnteringBlockedJunctionAllowed(segmentId, startNode, TernaryBool.Undefined);
                if (ExtNodeManager.JunctionHasOnlyHighwayRoads(nodeId)) {
                    JPMan.SetLaneChangingAllowedWhenGoingStraight(segmentId, startNode, TernaryBool.Undefined);
                }
                LaneArrowManager.Instance.ResetLaneArrows(segmentId, startNode);
                return true;
            });
        }

        public static void ClearRoad(List<ushort> segmentList) {
            foreach (ushort segmentId in segmentList) {
                foreach (bool startNode in Constants.ALL_BOOL) {
                    ushort nodeId = netService.GetSegmentNodeId(segmentId, startNode);
                    ClearNode(nodeId);
                }
            }
        }
    } //end class
}
