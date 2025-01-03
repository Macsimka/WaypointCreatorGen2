﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WaypointCreatorGen2
{
    public partial class WaypointCreator : Form
    {
        // Dictionary<UInt32 /*CreatureID*/, Dictionary<UInt64 /*lowGUID*/, List<WaypointInfo>>>
        private SortedDictionary<uint, Dictionary<ulong, List<WaypointInfo>>> WaypointDatabyCreatureEntry = new();

        private List<WaypointInfo> CopiedWaypoints;
        private List<WaypointInfo> CurrentWaypoints;


        public WaypointCreator()
        {
            InitializeComponent();
            GridViewContextMenuStrip.Enabled = false;
        }

        private void WaypointCreator_Load(object sender, EventArgs e)
        {

        }

        private async void EditorImportSniffButton_Click(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                Filter = @"txt files (*.txt)|*.txt|All files (*.*)|*.*",
                FilterIndex = 1,
                RestoreDirectory = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                EditorListBox.Items.Clear();
                EditorGridView.Rows.Clear();
                EditorWaypointChart.Series["Path"].Points.Clear();
                EditorWaypointChart.Series["Line"].Points.Clear();
                EditorImportSniffButton.Enabled = false;
                EditorFilterEntryButton.Enabled = false;
                GridViewContextMenuStrip.Enabled = false;
                EditorLoadingLabel.Text = @"Loading [" + Path.GetFileName(dialog.FileName) + @"]...";

                WaypointDatabyCreatureEntry = await Task.Run(() => GetWaypointDataFromSniff(dialog.FileName));

                EditorImportSniffButton.Enabled = true;
                EditorFilterEntryButton.Enabled = true;
                EditorLoadingLabel.Text = @"Loaded [" + Path.GetFileName(dialog.FileName) + @"].";
                ListEntries(0); // Initially listing all available GUIDs
            }
        }

        // Parses all waypoint data from the provided file and returns a container filled with all needed data
        private static SortedDictionary<uint, Dictionary<ulong, List<WaypointInfo>>> GetWaypointDataFromSniff(string filePath)
        {
            Dictionary<uint, Dictionary<ulong, List<WaypointInfo>>> result = [];

            using (StreamReader file = new(filePath))
            {
                while (file.ReadLine() is { } line)
                {
                    if (line.Contains("SMSG_ON_MONSTER_MOVE") || line.Contains("SMSG_ON_MONSTER_MOVE_TRANSPORT"))
                    {
                        WaypointInfo wpInfo = new();
                        uint creatureId = 0;
                        ulong lowGuid = 0;

                        // Extracting the packet timestamp in milliseconds from the packet header for delay calculations
                        string[] packetHeader = line.Split([' ']);
                        for (int i = 0; i < packetHeader.Length; ++i)
                        {
                            if (packetHeader[i].Contains("Time:"))
                            {
                                wpInfo.TimeStamp = uint.Parse(TimeSpan.Parse(packetHeader[i + 2]).TotalMilliseconds.ToString());
                                break;
                            }
                        }

                        // Header noted, reading rest of the packet now
                        do
                        {
                            // Skip chase movement
                            if (line.Contains("Face:") && line.Contains("FacingTarget"))
                                break;

                            // Extracting entry and lowGuid from packet
                            if (line.Contains("MoverGUID:"))
                            {
                                string[] words = line.Split([' ']);
                                for (int i = 0; i < words.Length; ++i)
                                {
                                    if (words[i].Contains("Entry:"))
                                        creatureId = uint.Parse(words[i + 1]);
                                    else if (words[i].Contains("Low:"))
                                        lowGuid = ulong.Parse(words[i + 1]);
                                }

                                // Skip invalid data.
                                if (creatureId == 0 || lowGuid == 0)
                                    break;
                            }

                            // Extracting spline duration
                            if (line.Contains("MoveTime:"))
                            {
                                string[] words = line.Split([' ']);
                                for (int i = 0; i < words.Length; ++i)
                                    if (words[i].Contains("MoveTime:"))
                                        wpInfo.MoveTime = uint.Parse(words[i + 1]);
                            }

                            // Extract Facing Angles
                            if (line.Contains("FaceDirection:"))
                            {
                                string[] words = line.Split([' ']);
                                for (int i = 0; i < words.Length; ++i)
                                    if (words[i].Contains("FaceDirection:"))
                                        wpInfo.Position.Orientation = float.Parse(words[i + 1], CultureInfo.InvariantCulture);
                            }

                            // Extracting waypoint (The space in the string is intentional. Do not remove!)
                            if (line.Contains(" Points:"))
                            {
                                string[] words = line.Split([' ']);
                                for (int i = 0; i < words.Length; ++i)
                                {
                                    if (words[i].Contains("X:"))
                                        wpInfo.Position.PositionX = float.Parse(words[i + 1], CultureInfo.InvariantCulture);
                                    else if (words[i].Contains("Y:"))
                                        wpInfo.Position.PositionY = float.Parse(words[i + 1], CultureInfo.InvariantCulture);
                                    else if (words[i].Contains("Z:"))
                                        wpInfo.Position.PositionZ = float.Parse(words[i + 1], CultureInfo.InvariantCulture);
                                }

                                // Delay Calculation
                                if (result.ContainsKey(creatureId) && result[creatureId].ContainsKey(lowGuid))
                                {
                                    if (result[creatureId][lowGuid].Count != 0)
                                    {
                                        int index = result[creatureId][lowGuid].Count - 1;
                                        long timeDiff = wpInfo.TimeStamp - result[creatureId][lowGuid][index].TimeStamp;
                                        uint oldMoveTime = result[creatureId][lowGuid][index].MoveTime;
                                        result[creatureId][lowGuid][index].Delay = Convert.ToInt32(timeDiff - oldMoveTime);
                                    }
                                }

                                // Everything gathered, time to store the data
                                if (!result.ContainsKey(creatureId))
                                    result.Add(creatureId, new());

                                if (!result[creatureId].ContainsKey(lowGuid))
                                    result[creatureId].Add(lowGuid, []);

                                result[creatureId][lowGuid].Add(wpInfo);
                            }

                            if (line.Contains(" WayPoints:"))
                            {
                                string[] words = line.Split([' ']);
                                SplinePosition splinePosition = new();
                                for (int i = 0; i < words.Length; ++i)
                                {
                                    if (words[i].Contains("X:"))
                                        splinePosition.PositionX = float.Parse(words[i + 1], CultureInfo.InvariantCulture);
                                    else if (words[i].Contains("Y:"))
                                        splinePosition.PositionY = float.Parse(words[i + 1], CultureInfo.InvariantCulture);
                                    else if (words[i].Contains("Z:"))
                                        splinePosition.PositionZ = float.Parse(words[i + 1], CultureInfo.InvariantCulture);
                                }

                                wpInfo.SplineList.Add(splinePosition);
                            }
                        }
                        while ((line = file.ReadLine()) != string.Empty);
                    }
                }
            }

            // Remove data with one or less points
            SortedDictionary<uint, Dictionary<ulong, List<WaypointInfo>>> finalResult = new();

            foreach (KeyValuePair<uint, Dictionary<ulong, List<WaypointInfo>>> entryPair in result)
            {
                foreach (KeyValuePair<ulong, List<WaypointInfo>> guidPair in entryPair.Value)
                {
                    if (guidPair.Value.Count > 2)
                    {
                        if (!finalResult.ContainsKey(entryPair.Key))
                            finalResult[entryPair.Key] = [];

                        if (!finalResult[entryPair.Key].ContainsKey(guidPair.Key))
                            finalResult[entryPair.Key][guidPair.Key] = [];

                        finalResult[entryPair.Key][guidPair.Key] = guidPair.Value;
                    }
                }
            }

            return finalResult;
        }

        private void ListEntries(uint creatureId)
        {
            EditorListBox.Items.Clear();

            if (creatureId == 0)
            {
                foreach (KeyValuePair<uint, Dictionary<ulong, List<WaypointInfo>>> waypointsByEntry in WaypointDatabyCreatureEntry)
                    foreach (KeyValuePair<ulong, List<WaypointInfo>> waypointsByGuid in waypointsByEntry.Value)
                        EditorListBox.Items.Add(waypointsByEntry.Key + " (" + waypointsByGuid.Key + ")");
            }
            else
            {
                if (WaypointDatabyCreatureEntry.TryGetValue(creatureId, out var value))
                    foreach (KeyValuePair<ulong, List<WaypointInfo>> waypointsByGuid in value)
                        EditorListBox.Items.Add(creatureId + " (" + waypointsByGuid.Key + ")");
            }
        }

        private void ShowWaypointDataForCreature(uint creatureId, ulong lowGUID)
        {
            if (!WaypointDatabyCreatureEntry.ContainsKey(creatureId))
                return;

            CurrentWaypoints = WaypointDatabyCreatureEntry[creatureId].ContainsKey(lowGUID)
                ? [.. WaypointDatabyCreatureEntry[creatureId][lowGUID]] // copy waypoint container
                : null;

            ShowWaypointDatas();
        }

        void ShowWaypointDatas()
        {
            // Filling the GridView
            EditorGridView.Rows.Clear();
            SplineGridView.Rows.Clear();

            if (CurrentWaypoints != null)
            {
                int count = 0;

                foreach (WaypointInfo wpInfo in CurrentWaypoints)
                {
                    int splineCount = 0;
                    string orientation = "NULL";
                    if (wpInfo.Position.Orientation.HasValue)
                        orientation = wpInfo.Position.Orientation.Value.ToString(CultureInfo.InvariantCulture);

                    EditorGridView.Rows.Add(
                        count,
                        wpInfo.Position.PositionX.ToString(CultureInfo.InvariantCulture),
                        wpInfo.Position.PositionY.ToString(CultureInfo.InvariantCulture),
                        wpInfo.Position.PositionZ.ToString(CultureInfo.InvariantCulture),
                        orientation,
                        wpInfo.MoveTime,
                        wpInfo.Delay);

                    foreach (SplinePosition splineInfo in wpInfo.SplineList)
                    {
                        SplineGridView.Rows.Add(
                            count,
                            splineCount,
                            splineInfo.PositionX.ToString(CultureInfo.InvariantCulture),
                            splineInfo.PositionY.ToString(CultureInfo.InvariantCulture),
                            splineInfo.PositionZ.ToString(CultureInfo.InvariantCulture));

                        ++splineCount;
                    }

                    ++count;
                }
            }

            BuildGraphPath();
            GridViewContextMenuStrip.Enabled = true;
        }

        private void BuildGraphPath()
        {
            EditorWaypointChart.ChartAreas[0].AxisX.ScaleView.ZoomReset();
            EditorWaypointChart.ChartAreas[0].AxisY.ScaleView.ZoomReset();

            EditorWaypointChart.Series["Path"].Points.Clear();
            EditorWaypointChart.Series["Line"].Points.Clear();

            foreach (DataGridViewRow dataRow in EditorGridView.Rows)
            {
                float x = float.Parse(dataRow.Cells[1].Value.ToString(), CultureInfo.InvariantCulture);
                float y = float.Parse(dataRow.Cells[2].Value.ToString(), CultureInfo.InvariantCulture);

                EditorWaypointChart.Series["Path"].Points.AddXY(x, y);
                EditorWaypointChart.Series["Path"].Points[dataRow.Index].Label = dataRow.Index.ToString();
                EditorWaypointChart.Series["Line"].Points.AddXY(x, y);
            }
        }

        // Filters the ListBox entries by CreatureID
        private void EditorFilterEntryButton_Click(object sender, EventArgs e)
        {
            uint.TryParse(EditorFilterEntryTextBox.Text, out uint creatureId);
            ListEntries(creatureId);
        }

        private void EditorListBox_SelectedValueChanged(object sender, EventArgs e)
        {
            if (EditorListBox.SelectedIndex == -1 || EditorListBox.SelectedItem == null)
                return;

            string str = EditorListBox.SelectedItem.ToString();
            if (string.IsNullOrEmpty(str))
                return;

            string[] words = str.Replace("(", "").Replace(")", "").Split([' ']);

            uint creatureId = uint.Parse(words[0]);
            ulong lowGuid = ulong.Parse(words[1]);
            ShowWaypointDataForCreature(creatureId, lowGuid);
        }

        private void CutStripMenuItem_Click(object sender, EventArgs e)
        {
            HandleDeleteItem();
        }

        private void RemoveDuplicates_Click(object sender, EventArgs e)
        {
            List<WaypointInfo> waypoints = [];

            foreach (WaypointInfo waypoint in CurrentWaypoints)
            {
                if (waypoint.HasOrientation())
                {
                    waypoints.Add(waypoint);
                    continue;
                }

                if (waypoints.All(compareWaypoint => !(waypoint.Position.GetExactDist2d(compareWaypoint.Position) <= 2.0f)))
                    waypoints.Add(waypoint);
            }

            CurrentWaypoints = waypoints;

            ShowWaypointDatas();
        }

        private void RemoveNearest_Click(object sender, EventArgs e)
        {
            bool canLoop = true;

            do
            {
                foreach (WaypointInfo wp in CurrentWaypoints)
                {
                    WaypointInfo nextWaypoint;

                    try
                    {
                        nextWaypoint = CurrentWaypoints[CurrentWaypoints.IndexOf(wp) + 1];
                    }
                    catch
                    {
                        canLoop = false;
                        break;
                    }

                    if (wp.Position.GetExactDist2d(nextWaypoint.Position) <= 10.0f && !nextWaypoint.HasOrientation())
                    {
                        CurrentWaypoints.RemoveAt(CurrentWaypoints.IndexOf(wp) + 1);
                        break;
                    }
                }
            }
            while (canLoop);

            ShowWaypointDatas();
        }

        private void CopyStripMenuItem_Click(object sender, EventArgs e)
        {
            if (EditorGridView.SelectedRows.Count == 0)
                return;

            CopiedWaypoints = [];

            for (int i = 0; i < EditorGridView.SelectedRows.Count; i++)
            {
                var row = EditorGridView.SelectedRows[i];
                CopiedWaypoints.Add(CurrentWaypoints[row.Index]);
            }
        }

        private void PasteAboveStripMenuItem_Click(object sender, EventArgs e)
        {
            PasteRows(true);
        }

        private void PasteBelowStripMenuItem_Click(object sender, EventArgs e)
        {
            PasteRows(false);
        }

        private void PasteRows(bool aboveSelection)
        {
            if (CopiedWaypoints == null || CopiedWaypoints.Count == 0 || EditorGridView.SelectedRows.Count == 0)
                return;

            int index = aboveSelection ? EditorGridView.SelectedRows[0].Index : EditorGridView.SelectedRows[^1].Index + 1;
            CurrentWaypoints.InsertRange(index, CopiedWaypoints);

            ShowWaypointDatas();
        }

        private void GenerateSQLStripMenuItem_Click(object sender, EventArgs e)
        {
            // Generates the SQL output.
            // waypoint_data
            SQLOutputTextBox.Clear();
            SQLOutputTextBox.AppendText("SET @WPGUID := xxxxxx;" + Environment.NewLine);
            SQLOutputTextBox.AppendText("SET @PATH := @WPGUID * 10;" + Environment.NewLine);
            SQLOutputTextBox.AppendText("UPDATE `creature` SET `spawndist` = 0, `MovementType` = 2 WHERE `guid` = @WPGUID;" + Environment.NewLine);

            // creature_addon
            SQLOutputTextBox.AppendText("REPLACE INTO `creature_addon` (`guid`, `waypointPathId`) VALUES (@WPGUID, @PATH);" + Environment.NewLine);

            SQLOutputTextBox.AppendText("DELETE FROM `waypoint_data` WHERE `id`= @PATH;" + Environment.NewLine);
            SQLOutputTextBox.AppendText("INSERT INTO `waypoint_data` (`id`, `point`, `position_x`, `position_y`, `position_z`, `orientation`, `delay`, `move_type`, `velocity`) VALUES" + Environment.NewLine);

            int rowCount = 0;

            foreach (DataGridViewRow row in EditorGridView.Rows)
            {
                ++rowCount;

                if (rowCount < EditorGridView.Rows.Count)
                    SQLOutputTextBox.AppendText($"(@PATH, {row.Cells[0].Value}, {row.Cells[1].Value}, {row.Cells[2].Value}, {row.Cells[3].Value}, {row.Cells[4].Value}, {row.Cells[6].Value}, 0, 0)," + Environment.NewLine);
                else
                    SQLOutputTextBox.AppendText($"(@PATH, {row.Cells[0].Value}, {row.Cells[1].Value}, {row.Cells[2].Value}, {row.Cells[3].Value}, {row.Cells[4].Value}, {row.Cells[6].Value}, 0, 0);" + Environment.NewLine);
            }

            SQLOutputTextBox.AppendText(Environment.NewLine);

            SQLOutputTextBox.AppendText("DELETE FROM `waypoint_data_addon` WHERE `PathID`= @PATH;" + Environment.NewLine);
            SQLOutputTextBox.AppendText("INSERT INTO `waypoint_data_addon` (`PathID`, `PointID`, `SplinePointIndex`, `PositionX`, `PositionY`, `PositionZ`) VALUES" + Environment.NewLine);

            int splineRowCount = 0;

            foreach (DataGridViewRow row in SplineGridView.Rows)
            {
                ++splineRowCount;

                SQLOutputTextBox.AppendText($"(@PATH, {row.Cells[0].Value}, {row.Cells[1].Value}, {row.Cells[2].Value}, {row.Cells[3].Value}, {row.Cells[4].Value})" +
                    $"{(splineRowCount < SplineGridView.Rows.Count ? "," : ";")}" + Environment.NewLine);
            }

            TabControl.SelectedTab = TabControl.TabPages[1];
        }

        private void SQLOutputSaveButton_Click(object sender, EventArgs e)
        {
            // Saving the text of the SQLOutputTextBox into a file
            SaveFileDialog dialog = new()
            {
                Filter = @"Structured Query Language (*.sql)|*.sql|All files (*.*)|*.*",
                FilterIndex = 1,
                DefaultExt = "sql",
                RestoreDirectory = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
                File.WriteAllText(dialog.FileName, SQLOutputTextBox.Text, System.Text.Encoding.UTF8);
        }

        private void EditorFilterEntryTextBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
                return;

            uint.TryParse(EditorFilterEntryTextBox.Text, out uint creatureId);
            ListEntries(creatureId);
        }

        private void EditorGridView_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
                HandleDeleteItem();
        }

        void HandleDeleteItem()
        {
            if (EditorGridView.SelectedRows.Count == 0)
                return;

            List<int> indexesToRemove = [];

            foreach (DataGridViewRow row in EditorGridView.SelectedRows)
                indexesToRemove.Add(row.Index);

            indexesToRemove.Sort();
            indexesToRemove.Reverse();

            foreach (int index in indexesToRemove)
                CurrentWaypoints.RemoveAt(index);

            ShowWaypointDatas();
        }
    }

    public class WaypointInfo
    {
        public uint TimeStamp;
        public WaypointPosition Position = new();
        public uint MoveTime;
        public int Delay;
        public List<SplinePosition> SplineList = [];

        public bool HasOrientation()
        {
            return Position.Orientation != null;
        }
    }

    public class WaypointPosition
    {
        public float PositionX;
        public float PositionY;
        public float PositionZ;
        public float? Orientation;

        public float GetExactDist2d(WaypointPosition comparePos)
        {
            return (float)Math.Sqrt(GetExactDist2dSq(this, comparePos));
        }

        public static double GetExactDist2dSq(WaypointPosition mainPos, WaypointPosition comparePos)
        {
            double dx = mainPos.PositionX - comparePos.PositionX; double dy = mainPos.PositionY - comparePos.PositionY;
            return dx * dx + dy * dy;
        }
    }

    public class SplinePosition
    {
        public float PositionX;
        public float PositionY;
        public float PositionZ;
    }
}
