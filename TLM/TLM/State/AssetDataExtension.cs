namespace TrafficManager.State {
    using System;
    using System.Collections.Generic;
    using ICities;
    using ColossalFramework.UI;
    using HarmonyLib;
    using CSUtil.Commons;
    using TrafficManager.Util;
    using static Util.Shortcuts;

    // Credits to boformer
    [HarmonyPatch(typeof(LoadAssetPanel), "OnLoad")]
    public static class OnLoadPatch {
        /// <summary>
        /// when loading asset from a file, IAssetData.OnAssetLoaded() is called for all assets but the one that is loaded from the file.
        /// this postfix calls IAssetData.OnAssetLoaded() for asset loaded from file.
        /// </summary>
        public static void Postfix(LoadAssetPanel __instance, UIListBox ___m_SaveList) {
            // Taken from LoadAssetPanel.OnLoad
            var selectedIndex = ___m_SaveList.selectedIndex;
            var getListingMetaDataMethod = typeof(LoadSavePanelBase<CustomAssetMetaData>).GetMethod(
                "GetListingMetaData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var listingMetaData = (CustomAssetMetaData)getListingMetaDataMethod.Invoke(__instance, new object[] { selectedIndex });

            // Taken from LoadingManager.LoadCustomContent
            if (listingMetaData.userDataRef != null) {
                AssetDataWrapper.UserAssetData userAssetData = listingMetaData.userDataRef.Instantiate() as AssetDataWrapper.UserAssetData;
                if (userAssetData == null) {
                    userAssetData = new AssetDataWrapper.UserAssetData();
                }
                AssetDataExtension.Instance.OnAssetLoaded(listingMetaData.name, ToolsModifierControl.toolController.m_editPrefabInfo, userAssetData.Data);
            }
        }
    }

    [Serializable]
    public class pathInfoExt {
        public ushort SegmentId;
        public ushort StartNodeId;
        public ushort EndNodeId;

        public void MapInstanceIDs(ushort newSegmentId, Dictionary<InstanceID, InstanceID> map) {
            map[new InstanceID { NetSegment = SegmentId }] =
                new InstanceID { NetSegment = newSegmentId };

            map[new InstanceID { NetNode = StartNodeId }] =
                new InstanceID { NetNode = newSegmentId.ToSegment().m_startNode };

            map[new InstanceID { NetNode = EndNodeId }] =
                new InstanceID { NetNode = newSegmentId.ToSegment().m_endNode };

            Log._Debug($"pathInfoExt.MapInstanceIDs: " +
                $"[{StartNodeId} .- {SegmentId} -. {EndNodeId}] -> " +
                $"[{newSegmentId.ToSegment().m_startNode} .- {newSegmentId} -. {newSegmentId.ToSegment().m_endNode}]");
        }

        public override string ToString() =>
            $"pathInfoExt[{StartNodeId} .- {SegmentId} -. {EndNodeId}]";
    }

    public class AssetDataExtension: AssetDataExtensionBase {
        public const string PATH_ID = "TMPE_oldPathInfoExts_V1.0";
        public const string NC_ID = "NodeController_V1.0";
        static Building[] buildingBuffer = BuildingManager.instance.m_buildings.m_buffer;

        public static AssetDataExtension Instance;
        public Dictionary<BuildingInfo, List<pathInfoExt>> Asset2PathInfoExts = new Dictionary<BuildingInfo, List<pathInfoExt>>();
        //public Dictionary<BuildingInfo, List<NodeData>> Asset2NodeDatas = new Dictionary<BuildingInfo, List<NodeData>>();

        public override void OnCreated(IAssetData assetData) {
            base.OnCreated(assetData);
            Instance = this;
        }

        public override void OnReleased() {
            Instance = null;
        }

        public override void OnAssetLoaded(string name, object asset, Dictionary<string, byte[]> userData) {
            Log.Info($"AssetDataExtension.OnAssetLoaded({name}, {asset}, userData) called");
            if (asset is BuildingInfo prefab) {
                Log._Debug("AssetDataExtension.OnAssetLoaded():  prefab is " + prefab);
                if (userData.TryGetValue(PATH_ID, out byte[] data1)) {
                    Log._Debug("AssetDataExtension.OnAssetLoaded():  extracted data for " + PATH_ID);
                    var oldPathInfoExts = SerializationUtil.Deserialize(data1) as List<pathInfoExt>;
                    AssertNotNull(oldPathInfoExts, "oldPathInfoExts");
                    Asset2PathInfoExts[prefab] = oldPathInfoExts;
                    Log._Debug("AssetDataExtension.OnAssetLoaded(): oldPathInfoExts=" + oldPathInfoExts.ToSTR());
                }
                if (userData.TryGetValue(NC_ID, out byte[] data2)) {
                    Log._Debug("AssetDataExtension.OnAssetLoaded():  extracted data for " + NC_ID);
                    var nodeDatas = SerializationUtil.Deserialize(data2) as List<NodeData>;
                    AssertNotNull(nodeDatas,"nodedatas");
                    Asset2NodeDatas[prefab] = nodeDatas;
                    Log._Debug("AssetDataExtension.OnAssetLoaded(): nodeDatas=" + nodeDatas.ToSTR());

                }
            }
        }

        public override void OnAssetSaved(string name, object asset, out Dictionary<string, byte[]> userData) {
            Log.Debug($"AssetDataExtension.OnAssetSaved({name}, {asset}, userData) called");
            userData = null;
            //var info = ToolsModifierControl.toolController.m_editPrefabInfo;
            if(asset is BuildingInfo prefab) {
                Log.Debug("AssetDataExtension.OnAssetSaved():  prefab is " + prefab);
                var oldPathInfoExts = GetOldNetworkIDs(prefab);
                userData = new Dictionary<string, byte[]>();
                userData.Add(PATH_ID, SerializationUtil.Serialize(oldPathInfoExts));
                Log.Debug("AssetDataExtension.OnAssetSaved(): oldPathInfoExts=" + oldPathInfoExts.ToSTR());

                List<NodeData> nodeDatas = NodeManager.Instance.GetNodeDataList();
                Log.Debug("AssetDataExtension.OnAssetSaved(): nodeDatas=" + nodeDatas.ToSTR());
                userData.Add(NC_ID, SerializationUtil.Serialize(nodeDatas));
            }
        }

        public static List<pathInfoExt> GetOldNetworkIDs(BuildingInfo info) {
            List<ushort> buildingSegmentIds = new List<ushort>();
            List<ushort> buildingIds = new List<ushort>(info.m_paths.Length);
            var oldInstanceIds = new List<pathInfoExt>();
            for (ushort buildingId = 1; buildingId < BuildingManager.MAX_BUILDING_COUNT; buildingId += 1) {
                if (buildingBuffer[buildingId].m_flags != Building.Flags.None) {
                    buildingSegmentIds.AddRange(BuildingDecoration.GetBuildingSegments(ref buildingBuffer[buildingId]));
                    buildingIds.Add(buildingId);
                }
            }
            for (ushort segmentId = 0; segmentId <NetManager.MAX_SEGMENT_COUNT; segmentId++) {
                if (netService.IsSegmentValid(segmentId)) {
                    if (!buildingSegmentIds.Contains(segmentId)) {
                        var item = new pathInfoExt {
                            SegmentId =  segmentId,
                            StartNodeId = segmentId.ToSegment().m_startNode,
                            EndNodeId = segmentId.ToSegment().m_endNode,
                        };
                        oldInstanceIds.Add(item);
                    }
                }
            }

            return oldInstanceIds;
        }
    }
}
