// ============================================================================
// CsvEditorTab.cs - CSV éƒ¨ç½²åˆ–(j9ï%º)Yš‚F Ğ¢òòÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓÓĞĞ¢òòˆÎ‹J<šåœ¨åº”ç”¨å†…åµŒä¸€ä¸ª CSV è¡¨æ ¼ç¼–è¾‘å™¨ï¼Œä¾›ç”¨æˆ·å¡«å†™æ‰¹é‡éƒ¨ç½²å€¹¥lB‹ËÃB‹ËÈ9¨.9l>X©şˆ;ŞûÉ Ğ¢òò)ÈRFFw&–Ef–Wrk‹.iLƒŠPƒ–>¿ò[¢úG¢†£š‚ó¾ò3–"_–’Ó’âë¦£öËš¢‡švÿ–¶_šº×¾ò!Y7–B4½%@½AT¿––¶c¶'¾ò$4(¼¼€€ƒŠrƒ–*ƒ¢öô€¼ƒ’şw–¶`MXƒŠPUQ´àİ¥Ñ 	=4ƒò[‚¢¾ï–d4(¼¼€€ƒŠrƒš¢‡švô¸Š/oH8 %9¡Ké¾z›®jŠiÛşKé¾ikyJh‹~X¢4(¼¼€€ƒŠrƒšVÃš6»š‚‡¦ª0ƒŠPƒ¦£öË–&7–~ë†–¶V®µæ£€æŸ¥
//
// è°å*9¥®{É®˜:{Û.zêybF"(i"8Î{ÉnŠG–"X¡¨ã€æŒ‰é’®
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using VirtualizationOpsTool.Core;
using VirtualizationOpsTool.Utils;

namespace VirtualizationOpsTool.UI
{
    public class CsvEditorTab
    {
        private readonly TabPage _parent;
        private readonly Form _root;
        private readonly Control _app;

        private DataGridView? _grid;
        private Button? _btnOpen;
        private Button? _btnSave;
        private Button? _btnAddRow;
        private Button? _btnDeleteRow;
        private Button? _btnReset;
        private Button? _btnSaveAs;
        private Label? _dirtyLabel;
        private Label? _statusLabel;

        private string _currentPath = "";
        private bool _dirty = false;
        private List<string> _columns = new List<stringÈ“˜[YH‹’T‹“X\ÚÈ‹‘Ø]]Ø^H‹ÔH‹“Y[[ÜH‹•[\]H‹•›[ˆ‹’Üİ‹‘\ÚĞØ\XÚ]QĞˆ‹‘]\İÜ™HˆNÃBˆš]˜]H\İ\İİš[™ÏˆÜ›İÜÈH™]È\İ\İİš[™Ï‚‚“°Ğ Ğ¢&—fFR7FF–2&VFöæÇ’†6…6WCÇ7G&–æp%¹½É•½±Õµ¹Ì€ô¹•Ü!…Í¡M•ĞñÍÑÉ¥¹œ(StringComparer.OrdinalIgnoreCase) { "Rule" };

        private static readonly Dictionary<string, intÛÛ[[•ÚYX\H™]ÈXİ[Û˜\Oİš[™Ë[‚“°Ğ Ğ¢V&Æ–277dVF—F÷%F"…F%vR&VçBÂf÷&Ò&ö÷BÂ6öçG&öÂĞ¢°Ğ¢÷&VçBÒ&VçC°Ğ¢÷&ö÷BÒ&ö÷C°Ğ¢öÒ°Ğ¢ö7W'&VçEF‚ÒvWDFVfVÇD77eF‚‚“°Ğ¢'V–ÆET’‚“°Ğ¢ÆöD77b…ö7W'&VçEF‚“°Ğ¢ĞĞ Ğ¢&—fFR7G&–ærvWDFVfVÇD77eF‚‚Ğ¢°Ğ¢&WGW&âF‚ä6öÖ&–æR„6÷&UWF–Ç2ävWEW6W$FFF—"‚’Â&FWÆ÷•÷fÕöÆ—7Bæ77b"“°Ğ¢ĞĞ Ğ¢&—fFRfö–B'V–ÆET’‚Ğ¢°Ğ¢f"g&ÖRÒæWrF&ÆTÆ–÷WEæVÀĞ¢°Ğ¢Fö6²ÒFö6µ7G–ÆRäf–ÆÂÀĞ¢FF–ærÒFF–æräV×G’Â6öÇVÖä6÷VçBÒÂ&÷t6÷VçBÒ0Ğ¢Ó°Ğ¢g&ÖRä6öÇVÖå7G–ÆW2äFB†æWr6öÇVÖå7G–ÆR…6—¦UG—RåW&6VçBÂb’“°Ğ¢g&ÖRå&÷u7G–ÆW2äFB†æWr&÷u7G–ÆR…6—¦UG—Rä'6öÇWFRÂG•WF–Ç2å‚ƒCB’’“°Ğ¢g&ÖRå&÷u7G–ÆW2äFB†æWr&÷u7G–ÆR…6—¦UG—Rä'6öÇWFRÂG•WF–Ç2å‚ƒ#B’’“°Ğ¢g&ÖRå&÷u7G–ÆW2äFB†æWr&÷u7G–ÆR…6—¦UG—RåW&6VçBÂb’“°Ğ Ğ¢f"FööÆ&"ÒæWrfÆ÷tÆ–÷WEæVÀĞ¢°Ğ¢Fö6²ÒFö6µ7G–ÆRäf–ÆÂÀĞ¢w&6öçFVçG2ÒfÇ6RÀĞ¢fÆ÷tF—&V7F–öâÒfÆ÷tF—&V7F–öâäÆVgEFõ&–v‡BÀĞ¢FF–ærÒFF–æräV×G’ÀĞ¢Ö&v–âÒæWrFF–ærƒbÂbÂbÂ"Ğ¢Ó°Ğ Ğ¢ö'Fä÷VâÒæWr'WGFöâ²FW‡BÒ.ZûÎXZR"ÂfÆE7G–ÆRÒfÆE7G–ÆRäfÆBÂÖ&v–âÒæWrFF–ærƒ"’ÂWFõ6—¦RÒG'VRÂÖ–æ–×VÕ6—¦RÒæWr6—¦RƒcÂ’Ó°Ğ¢ö'Fä÷Vâä6Æ–6²³Ò'Fä–×÷'Eô6Æ–6³°Ğ¢FööÆ&"ä6öçG&öÇ2äFB…ö'Fä÷Vâ“°Ğ Ğ¢ö'Få6fRÒæWr'WGFöâ²FW‡BÒ.KùŞZÙ‚"ÂfÆE7G–ÆRÒfÆE7G–ÆRäfÆBÂÖ&v–âÒæWrFF–ærƒ"’ÂWFõ6—¦RÒG'VRÂÖ–æ–×VÕ6—¦RÒæWr6—¦RƒcÂ’Ó°Ğ¢ö'Få6fRä6Æ–6²³Ò'Få6fUô6Æ–6³°Ğ¢FööÆ&"ä6öçG&öÇ2äFB…ö'Få6fR“°Ğ Ğ¢f"'FäVF—BÒæWr'WGFöâ²FW‡BÒ.{Én‹é"ÂfÆE7G–ÆRÒfÆE7G–ÆRäfÆBÂÖ&v–âÒæWrFF–ærƒ"’ÂWFõ6—¦RÒG'VRÂÖ–æ–×VÕ6—¦RÒæWr6—¦RƒcÂ’Ó°Ğ¢'FäVF—Bä6Æ–6²³Ò'FäVF—DW‡FW&æÅô6Æ–6³°Ğ¢FööÆ&"ä6öçG&öÇ2äFB†'FäVF—B“°Ğ Ğ¢ö'FäFE&÷rÒæWr'WGFöâ²FW‡BÒ.ikZ)îŠÂ"ÂfÆE7G–ÆRÒfÆE7G–ÆRäfÆBÂÖ&v–âÒæWrFF–ærƒ"’ÂWFõ6—¦RÒG'VRÂÖ–æ–×VÕ6—¦RÒæWr6—¦RƒsÂ’Ó°Ğ¢ö'FäFE&÷rä6Æ–6²³Ò'FäFE&÷uô6Æ–6³°Ğ¢FööÆ&"ä6öçG&öÇ2äFB…ö'FäFE&÷r“°Ğ Ğ¢ö'FäFVÆWFU&÷rÒæWr'WGFöâ²FW‡BÒ.XŠ™šN˜KŠŞŠÂ"ÂfÆE7G–ÆRÒfÆE7G–ÆRäfÆBÂÖ&v–âÒæWrFF–ærƒ"’ÂWFõ6—¦RÒG'VRÂÖ–æ–×VÕ6—¦RÒæWr6—¦Rƒ“Â’Ó°Ğ¢ö'FäFVÆWFU&÷rä6Æ–6²³Ò'FäFVÆWFU&÷uô6Æ–6³°Ğ¢FööÆ&"ä6öçG&öÇ2äFB…ö'FäFVÆWFU&÷r“°Ğ Ğ¢ö'Få&W6WBÒæWr'WGFöâ²FW‡BÒ.˜xŞ{ÚîK‹®jŠiÛò"ÂfÆE7G–ÆRÒfÆE7G–ÆRäfÆBÂÖ&v–âÒæWrFF–ærƒ"’ÂWFõ6—¦RÒG'VRÂÖ–æ–×VÕ6—¦RÒæWr6—¦Rƒ“Â’Ó°Ğ¢ö'Få&W6WBä6Æ–6²³Ò'Få&W6WEô6Æ–6³°Ğ¢FööÆ&"ä6öçG&öÇ2äFB…ö'Få&W6WB“°Ğ Ğ¢ö'Få6fT2ÒæWr'WGFöâ²FW‡BÒ.XúnZÙK‹¢"ÂfÆE7G–ÆRÒfÆE7G–ÆRäfÆBÂÖ&v–âÒæWrFF–ærƒ"’ÂWFõ6—¦RÒG'VRÂÖ–æ–×VÕ6—¦RÒæWr6—¦RƒƒÂ’Ó°Ğ¢ö'Få6fT2ä6Æ–6²³Ò'Få6fT5ô6Æ–6³°Ğ¢FööÆ&"ä6öçG&öÇ2äFB…ö'Få6fT2“°Ğ Ğ¢öF—'G”Æ&VÂÒæWrÆ&VÂ²FW‡BÒ""ÂWFõ6—¦RÒG'VRÂf÷&T6öÆ÷"Ò6öÆ÷"äg&öÔ&v"ƒ#3’Â‚Â’ÂÖ&v–âÒæWrFF–ærƒB’Ó°Ğ¢FööÆ&"ä6öçG&öÇ2äFB…öF—'G”Æ&VÂ“°Ğ Ğ¢÷7FGW4Æ&VÂÒæWrÆ&VÂ²FW‡BÒ.hùzK®òk–>3–å•å…¨/9æí9£©yï%º)ò1¹Ñ•Èƒ†»¢º“¼ŒEsc å–æ¶ˆï¼›Delete åˆ é™¤é€‰ä¸­è¡Œ", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(85, 85, 85), AutoSize = true, Padding = new Padding(0, 4, 0, 4) };

            var gridContainer = new Panel { Dock = DockStyle.Fill };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToResizeColumns = true,
                ReadOnly = false,
                MultiSelect = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei", 9),
                BackgroundColor = SystemColors.Window,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    Font = new Font("Microsoft YaHei", 9, FontStyle.Bold),
                    BackColor = Color.FromArgb(240, 240, 240)
                },
                EnableHeadersVisualStyles = false
            };
            _grid.CellValueChanged += Grid_CellValueChanged;
            _grid.KeyDown += Grid_KeyDown;
            _grid.ColumnWidthChanged += Grid_ColumnWidthChanged;
            _grid.SizeChanged += Grid_SizeChanged;
            gridContainer.Controls.Add(_grid);

            frame.Controls.Add(toolbar);
            frame.SetRow(toolbar, 0);
            frame.Controls.Add(_statusLabel);
            frame.SetRow(_statusLabel, 1);
            frame.Controls.Add(gridContainer);
            frame.SetRow(gridContainer, 2);

            _parent.Controls.Add(frame);

            UpdatePathLabel();
        }

        private void BtnImport_Click(object? sender, EventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "CSV æ–….íŸ
‹˜Üİˆ‹]HH¹kï9aiHÔÕˆ9¥¡ù.íˆˆNÃBˆYˆ
Ë”ÚİÑX[ÙÊ
HOHX[ÙÔ™\İ[“ÒÊCBˆÃBˆÛÛ[[•ÚYX\ÛX\Š
NÃBˆØYÜİŠË‘š[S˜[YJNÃBˆ\]T]X™[

NÃBˆYˆ
Üİ]\ÓX™[OH[
CBˆÃBˆÜİ]\ÓX™[•^H	¼'äéH9mì¹kï9ai{É§µF‚ävWDf–ÆTæÖR†FÆräf–ÆTæÖR—ŞûÈKw–¶c–/î#–g–—¦îc¢º“šZ’îÛ¾ò$ˆì4(€€€€€€€€€€€€€€€€€€€}ÍÑ…ÑÕÍ1…‰•°¹½É•½±½È€ô½±½È¹É½µÉˆ ÈÄ°€ÄÀÄ°€ÄäÈ¤ì4(€€€€€€€€€€€€€€€ô4(€€€€€€€€€€€ô4(€€€€€€€ô4(4(€€€€€€€ÁÉ¥Ù…Ñ”Ù½¥	Ñ¹M…Ù•}±¥¬¡½‰©•ĞüÍ•¹‘•È°Ù•¹ÑÉÌ”¤4(€€€€€€€ì4(€€€€€€€€€€€M…Ù•ÍØ ¤ì4(€€€€€€€ô4(4(€€€€€€€ÁÉ¥Ù…Ñ”Ù½¥	Ñ¹‘¥ÑáÑ•É¹…±}±¥¬¡½‰©•ĞüÍ•¹‘•È°Ù•¹ÑÉÌ”¤4(€€€€€€€ì4(€€€€€€€€€€€¥˜€¡ÍÑÉ¥¹œ¹%Í9Õ±±=ÉµÁÑä¡}ÕÉÉ•¹ÑA…Ñ ¤ñğ€…¥±”¹á¥ÍÑÌ¡}ÕÉÉ•¹ÑA…Ñ ¤¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€Q½…ÍÑUÑ¥±Ì¹M¡½İQ½…ÍĞ¡}É½½Ğ°€‹¢¾ß–#š&O–òš"[’şw–¶c’â’â¨MXƒšZ»¶");
                return;
            }

            // å¦‚æœç¨‹åºå†…æœ‰æœªä·ykf9æ¡9.îiKûÈÎXXhC’ëR£š"ß’İå­˜
            if (_dirty)
            {
                var result = MessageBox.Show(
                    "å½“å‰æœ‰æœªä¿å­˜çš„ä¿®æ”¹ï¼Œæ˜¯å¦å…ˆä¿å­˜åˆ°æ–‡ä»¶ï¼Ÿ\n\né€‰æ‹©ã€Œæ˜¯ã€å°†å…ˆä·ykf9odùbcy.îiKûÈÎXhŞyJZIn˜:{Én‹éYšh™>[È8%Æî˜hº8ÎY
n8Ş[ny»Nhê^h™>[ÈZIn˜:{Én‹éYšûÈÎKØnzˆ¾[¨şXh^y¨NKúîiKKÉ®KŠ.ZK8""ÀĞ¢.hC’èˆ°4(€€€€€€€€€€€€€€€€€€€5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹e•Í9½…¹•°°4(€€€€€€€€€€€€€€€€€€€5•ÍÍ…•	½á%½¸¹EÕ•ÍÑ¥½¸¤ì4(4(€€€€€€€€€€€€€€€¥˜€¡É•ÍÕ±Ğ€ôô¥…±½I•ÍÕ±Ğ¹…¹•°¤É•ÑÕÉ¸ì4(€€€€€€€€€€€€€€€¥˜€¡É•ÍÕ±Ğ€ôô¥…±½I•ÍÕ±Ğ¹e•Ì¤4(€€€€€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€€€€M…Ù•ÍØ ¤ì4(€€€€€€€€€€€€€€€ô4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€ÑÉä4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€¼¼ƒ¢ºÃ–öW’ş»šRçš^Û¦^Ó¼Œç”¨äºæ£€æµ‰¥¡K»niŠÖB›¢Š¤¿å­˜
                var originalMtime = File.GetLastWriteTime(_currentPath);
                Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _currentPath,
                    UseShellExecute = true
                });
                if (_statusLabel != null)
                {
                    _statusLabel.Text = " å·²åœ¨å¤–éƒ¨ç¼–è¤yfj9.+y¢d[Èò3¶'–…ä¿å­˜åè‡ªåŠ¨åŠ è½½â€¦";
                    _statusLabel.ForeColor = Color.FromArgb(21, 101, 192);
                }
                var watchThread = new System.Threading.Thread(() => WatchExternalEdit(originalMtime))
                {
                    IsBackground = true
                };
                watchThread.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"æ— æ³•æ‰“å¼€æ–‡ä»¶ï¼š\n{ex.Message}", "é”™è¯¯", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void WatchExternalEdit(DateTime originalMtime)
        {
            try
            {
                while (true)
                {
                    System.Threading.Thread.Sleep(500);
                    if (!File.Exists(_currentPath)) continue;

                    var currentMtime = File.GetLastWriteTime(_currentPath);
                    if (currentMtime > originalMtime)
                    {
                        _root.Invoke(new Action(() =>
                        {
                            try
                            {
                                ReloadAfterExternalEdit();
                            }
                            catch
                            {
                            }
                        }));
                        break;
                    }
                }
            }
            catch
            {
            }
        }

        private void ReloadAfterExternalEdit()
        {
            LoadCsv(_currentPath, silent: true);
            _dirty = false;
            UpdatePathLabel();
            if (_statusLabel != null)
            {
                _statusLabel.Text = $"âœ… å·²ä»å¤–éƒ¨ç¼–è¾‘å™¨é‡æ–°åŠ è½½ï¼š{Path.GetFileName(_currentPath)}";
                _statusLabel.ForeColor = Color.FromArgb(46, 125, 50);
            }
        }

        private void BtnAddRow_Click(object? sender, EventArgs e)
        {
            if (_grid == null) return;

            var values = new object[_grid.Columns.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = "";
            }
            int rowIndex = _grid.Rows.Add(values);
            _grid.ClearSelection();
            _grid.Rows[rowIndex].Selected = true;
            MarkDirty();
        }

        private void BtnDeleteRow_Click(object? sender, EventArgs e)
        {
            if (_grid == null) return;

            var selectedRows = _grid.SelectedRows.Cast<DataGridViewRow>().OrderByDescending(r =‹’[™^
K•Ó\İ

NÃBˆ›Ü™XXÚ
˜\ˆ›İÈ[ˆÙ[XİY›İÜÊCBˆÃBˆÙÜšY”›İÜË”™[[İ™J›İÊNÃBˆCBˆX\šÑ\J
NÃBˆCBƒBˆš]˜]H›ÚY”™\Ù]ĞÛXÚÊØš™XİÈÙ[™\‹]™[\™ÜÈJCBˆÃBˆYˆ
Y\ÜØYÙP›Ş”ÚİÊ¹èk¹k¦º) zaãyïk¹..¹ª(y§oùd%ûï'ùodùbcy.îiK[nKŠ.ZK8""Â.zîŠêB"ÂÖW76vT&÷„'WGFöç2å–W4æò’ÓÒF–Æöu&W7VÇBå–W2Ğ¢°Ğ¢öw&–Còå&÷w2ä6ÆV"‚“°Ğ¢öF—'G’ÒfÇ6S°Ğ¢WFFUF„Æ&VÂ‚“°Ğ¢ĞĞ¢ĞĞ Ğ¢&—fFRfö–B'Få6fT5ô6Æ–6²†ö&¦V7Cò6VæFW"ÂWfVçD&w2RĞ¢°Ğ¢–b…öw&–BÓÒçVÆÂÇÂöw&–Bå&÷w2ä6÷VçBÓÒĞ¢°Ğ¢Fö7EWF–Ç2å6†÷uFö7B…÷&ö÷BÂ.[Ù>X˜Şk*iÈX¿’İå­˜çš„æ•°æ®");
                return;
            }

            string vcIp = SessionManager.Instance.CurrentVc ?? "unknown";
            string safeVc = vcIp.Replace(":", "-").Replace("/", "-");

            using var dlg = new SaveFileDialog
            {
                Filter = "CSV æ–‡ä»¶|*.csv",
                Title = "å¦å­˜ä¸º CSV æ–….íˆ‹Bˆš[S˜[YHH	•›SÜË^ÜØY™U˜ßK˜ÜİˆƒBˆNÃBƒBˆYˆ
Ë”ÚİÑX[ÙÊ
HOHX[ÙÔ™\İ[“ÒÊCBˆÃBˆCBˆÃBˆØ]™PÜİ•Ô]
Ë‘š[S˜[YJNÃBˆÙ\HH˜[ÙNÃBˆ\]T]X™[

NÃBˆYˆ
Üİ]\ÓX™[OH[
CBˆÃBˆÜİ]\ÓX™[•^H	¸§!H9mì¹bnZÙK‹®ZH~K»ŞûÉ§µF‚ävWDf–ÆTæÖR†FÆräf–ÆTæÖR—ŞûÈ[Ù>X˜Ş{Én‹éih~K»nKˆŞXùûÈ’#°Ğ¢÷7FGW4Æ&VÂäf÷&T6öÆ÷"Ò6öÆ÷"äg&öÔ&v"ƒCbÂ#RÂS“°Ğ¢ĞĞ¢ĞĞ¢6F6‚„W†6WF–öâW‚Ğ¢°Ğ¢ÖW76vT&÷‚å6†÷r‚B.Kw–¶c–’Ç¢Ò—¾òiq¹í•à¹5•ÍÍ…•ôˆ°€‹¦Rg¢¯", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            MarkDirty();
        }

        private void Grid_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                BtnDeleteRow_Click(null, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void Grid_ColumnWidthChanged(object? sender, DataGridViewColumnEventArgs e)
        {
            if (e.Column is DataGridViewColumn col && !string.IsNullOrEmpty(col.Name))
            {
                ColumnWidthMap[col.Name] = col.Width;
            }
        }

        private void Grid_SizeChanged(object? sender, EventArgs e)
        {
            ApplyAutoResizeWithMemory();
        }

        private void ApplyAutoResizeWithMemory()
        {
            if (_grid == null || _grid.Columns.Count == 0) return;

            _grid.SuspendLayout();

            var savedWidths = new Dictionary<string, int>();
            foreach (DataGridViewColumn col in _grid.Columns)
            {
                if (ColumnWidthMap.TryGetValue(col.Name, out var w))
                {
                    savedWidths[col.Name] = w;
                }
            }

            _grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);

            foreach (var kvp in savedWidths)
            {
                if (_grid.Columns.Contains(kvp.Key))
                {
                    _grid.Columns[kvp.Key].Width = kvp.Value;
                }
            }

            _grid.ResumeLayout();
        }

        private void LoadCsv(string filePath, bool silent = false)
        {
            // å…ˆè®ùanX‹K‹Ni{nXc¦?¾ò3š"C–*–B;–7šnÿš6‹¾ò3¦ÿ–7¢»å…¹i,z-)y¥í¹¥l9£k¹.(¹i,CBˆ˜\ˆ™]ĞÛÛ[[œÈH™]È\İİš[™ÏŠØÛÛ[[œÊNÃBˆ˜\ˆ™]Ô›İÜÈH™]È\İ\İİš[™Ï‚‚“°Ğ Ğ¢–b‚7G&–ærä—4çVÆÄ÷$V×G’†f–ÆUF‚’bbf–ÆRäW†—7G2†f–ÆUF‚’Ğ¢°Ğ¢G'Ğ¢°Ğ¢òòXXŠû–>[–:u§‰keˆ¨.ò3–7š‚çš6»ò[‚¢‡ 
                    byte[] fileBytes;
                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        fileBytes = new byte[fs.Length];
                        fs.Read(fileBytes, 0, (int)fs.Length);
                    }

                    // æ£€æµ‹ BOM
                    System.Text.Encoding detectedEncoding = DetectEncoding(fileBytes);

                    // ç”¨æ£€æµ‹åˆ°çš„ç¼–ç å°†å­—èŠ‚è½¬ä¸ºå­—ç¬¦ä¸²
                    string fileContent = detectedEncoding.GetString(fileBytes);
                    // å»é™¤ UTF-8 BOMï"Q‘Q‘»Èkiù<Œé½aczi¥¹b%ŠZKNŠ*šÆ‡š~E¯¼è‡´å­•«­yd#y.#yc.zacCBˆYˆ
š[PÛÛ[“[™İˆ	‰ˆš[PÛÛ[ÌHOH	×Q‘Q‘‰ÊCBˆš[PÛÛ[Hš[PÛÛ[”İXœİš[™ÊJNÃBƒBˆËÈ9£"z(c:)èié55`Ğ¢f"Æ–æW2Òf–ÆT6öçFVçBå7Æ—B†æWuµÒ²%Ç%Æâ"Â%Æâ"ÒÂ7G&–æu7Æ—D÷F–öç2äæöæRĞ¢åv†W&R†ÂÓâ7G&–ærä—4çVÆÄ÷%v†—FU76R†Â’Ğ¢åFôÆ—7B‚“°Ğ Ğ¢–b†Æ–æW2ä6÷VçB €À¤4(€€€€€€€€€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€€€€€€€€Ù…ÈÉ…İ½±Õµ¹Ì€ô¹•Ü1¥ÍĞñÍÑÉ¥¹œø¡±¥¹•ÍlÁt¹MÁ±¥Ğ œ°œ¤¤ì4(€€€€€€€€€€€€€€€€€€€€€€€™½È€¡¥¹Ğ¤€ô€Àì¤€ğÉ…İ½±Õµ¹Ì¹½Õ¹Ğì¤¬¬¤4(€€€€€€€€€€€€€€€€€€€€€€€€€€€É…İ½±Õµ¹Ím¥t€ôÉ…İ½±Õµ¹Ím¥t¹QÉ¥´ ¤ì4(4(€€€€€€€€€€€€€€€€€€€€€€€Ù…È­••Á%‘à€ô¹•Ü1¥ÍĞñ¥¹Ğ();
                        for (int i = 0; i < rawColumns.Count; i++)
                        {
                            if (!IgnoreColumns.Contains(rawColumns[i]))
                                keepIdx.Add(i);
                        }
                        newColumns = keepIdx.Select(i => rawColumns[i]).ToList();

                        for (int i = 1; i < lines.Count; i++)
                        {
                            var fields = lines[i].Split(',');
                            var row = new List<string>();
                            foreach (int ci in keepIdx)
                            {
                                row.Add(ci < fields.Length ? fields[ci] : "");
                            }
                            newRows.Add(row);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!silent)
                    {
                        MessageBox.Show($"åŠ è½½ CSV å¤±è´¥: {ex.Message}\n\nå½“å‰æ•°æ®ä¿æŒä¸å˜ã€‚", "é”™è«È‹Y\ÜØYÙP›Ş]ÛœË“ÒËY\ÜØYÙP›ŞXÛÛ‹‘\œ›ÜŠNÃBˆCBˆ™]\›ÈËÈ:+îXùnZK‹J^ò3’şwš2–:vœ‰æ•°æ®ä¸å˜
                }
            }

            // è®ùanh‰X©şûÈÎi»şhÚ.i[hÚàĞ¢ö6öÇVÖç2ÒæWt6öÇVÖç3°Ğ¢÷&÷w2ÒæWu&÷w3°Ğ¢WFFTw&–B‚“°Ğ¢öF—'G’ÒfÇ6S°Ğ¢WFFUF„Æ&VÂ‚“°Ğ¢ĞĞ Ğ¢òòòÇ7VÖÖ'“àĞ¢òòòj8kX¾ih~K»n{ÉnzûÉ®KÉXX‚$ôŞò3š^€	=4ƒš^Û¦k¢Çå†…å®¹ç‰¹å¾åˆ¤æ–­ UTF-8 è¿˜æ˜¯ GBK
        /// </summary>
        private System.Text.Encoding DetectEncoding(byte[] bytes)
        {
            if (bytes.Length == 0)
                return System.Text.Encoding.UTF8;

            // æ£€æµ‹ UTF-8 BOM
            if (bytes.Length HÈ	‰ˆ]\ÖÌHOHQˆ	‰ˆ]\ÖÌWHOHˆ	‰ˆ]\ÖÌ—HOH‘ŠCBˆ™]\›ˆŞ\İ[K•^‘[˜ÛÙ[™Ë•UÃBƒBˆËÈ9¨à9­bÈU‹LMˆH“ÓCBˆYˆ
]\Ë“[™İÒ"bb'—FW5³ÒÓÒ„dbbb'—FW5³ÒÓÒ„dRĞ¢&WGW&â7—7FVÒåFW‡BäVæ6öF–æråVæ–6öFS°Ğ Ğ¢òòj8kX²UDbÓb$R$ôĞĞ¢–b†'—FW2äÆVæwF‚ãÒ"bb'—FW5³ÒÓÒ„dRbb'—FW5³ÒÓÒ„dbĞ¢&WGW&â7—7FVÒåFW‡BäVæ6öF–ærä&–tVæF–åVæ–6öFS°Ğ Ğ¢òòiz$ôÒi{nûÈÎ˜	®‹–¶_¢*&ç–ú–"“šZ´UQ´àƒ¢şcšb¼	,4(€€€€€€€€€€€¥˜€¡%ÍY…±¥‘UÑ˜à¡‰åÑ•Ì¤¤4(€€€€€€€€€€€€€€€É•ÑÕÉ¸MåÍÑ•´¹Q•áĞ¹¹½‘¥¹œ¹UQàì4(4(€€€€€€€€€€€€¼¼ƒ’â7šb¶œ‰æ•ˆ UTF-8ï#9£"yìî{¹æîc¢º“ò[‚¼ˆGBKÈZHNy`Ğ¢&WGW&â7—7FVÒåFW‡BäVæ6öF–æräFVfVÇC°Ğ¢ĞĞ Ğ¢òòòÇ7VÖÖ'4(€€€€€€€€¼¼¼ƒ–"“šZ·–¶_¢*šVÃîšb¿–B›’âëšr'šV#jUQ´àƒò[‚4(€€€€€€€€¼¼¼€ğ½ÍÕµµ…Éä
        private bool IsValidUtf8(byte[] bytes)
        {
            int i = 0;
            while (i < bytes.Length)
            {
                byte b = bytes[i];

                // ASCII (0x00-0x7F)
                if (b <= 0x7F)
                {
                    i++;
                    continue;
                }

                // å¤šå­—èŠ‚ UTF-8 åºåˆ—
                int continuationBytes = 0;
                if ((b & 0xE0) == 0xC0) continuationBytes = 1;      // 2-byte sequence
                else if ((b & 0xF0) == 0xE0) continuationBytes = 2; // 3-byte sequence
                else if ((b & 0xF8) == 0xF0) continuationBytes = 3; // 4-byte sequence
                else return false; // Invalid leading byte

                // æ£€æŸ¥åç»­å­–" ƒBˆ›Üˆ
[ˆHNÈˆHÛÛ[X][Û]\ÎÈŠÊÊCBˆÃBˆYˆ
H
ÈˆH]\Ë“[™İ
H™]\›ˆ˜[ÙNÃBˆYˆ

]\ÖÚH
È—H	ˆÌ
HOH
H™]\›ˆ˜[ÙNÃBˆCBƒBˆH
ÏHH
ÈÛÛ[X][Û]\ÎÃBˆCBˆ™]\›ˆYNÃBˆCBƒBˆš]˜]H›ÚYØ]™PÜİŠ
CBˆÃBˆYˆ
İš[™Ë’\Ó[Ü‘[\JØİ\œ™[]
JCBˆÃBˆ˜\ˆÈH™]ÈØ]™Qš[QX[ÙÈÈš[\ˆHÔÕˆ9¥¡K»gÂ¢æ77b"ÂF—FÆRÒ.KùŞZÙ‚55bih~K»b"Ó°Ğ¢–b†FÆrå6†÷tF–Æör‚’ÒF–Æöu&W7VÇBäô²’&WGW&ã°Ğ¢ö7W'&VçEF‚ÒFÆräf–ÆTæÖS°Ğ¢ĞĞ Ğ¢G'Ğ¢°Ğ¢öw&–CòäVæDVF—B‚“°Ğ Ğ¢òòKùŞZÙK‹¢UDbÓ‚v—F‚$ôŞò3†»’ştá•°ƒ¢÷š¶†»¢¾–"¯’â·šZò[‚4(€€€€€€€€€€€€€€€ÕÍ¥¹œÙ…ÈİÉ¥Ñ•È€ô¹•ÜMÑÉ•…µ]É¥Ñ•È¡}ÕÉÉ•¹ÑA…Ñ °™…±Í”°¹•ÜMåÍÑ•´¹Q•áĞ¹UQá¹½‘¥¹œ¡ÑÉÕ”¤¤ì4(4(€€€€€€€€€€€€€€€€¼¼ƒ’î8}É¥ƒ¢¾ï–>[¢†£–’Ó¾ò#šR¿š2R£š"ß¢ÂšVÓ–"_¦†ë–ê?¾ò$4(€€€€€€€€€€€€€€€Ù…È¡•…‘•ÉÌ€ô¹•Ü1¥ÍĞñÍÑÉ¥¹œø ¤ì4(€€€€€€€€€€€€€€€™½É•… €¡…Ñ…É¥‘Y¥•İ½±Õµ¸½°¥¸}É¥¹½±Õµ¹Ì¤4(€€€€€€€€€€€€€€€€€€€¡•…‘•ÉÌ¹‘¡½°¹9…µ”¤ì4(€€€€€€€€€€€€€€€İÉ¥Ñ•È¹]É¥Ñ•1¥¹”¡ÍÑÉ¥¹œ¹)½¥¸ ˆ°ˆ°¡•…‘•ÉÌ¤¤ì4(4(€€€€€€€€€€€€€€€€¼¼ƒ’î8}É¥ƒ¢¾å–æ•°æ®è¡Œ
                foreach (DataGridViewRow gridRow in _grid.Rows)
                {
                    if (gridRow.IsNewRow) continue;
                    var values = new List<string

NÃBˆ›Üˆ
[HHÈHÙÜšYÛÛ[[œËÛİ[ÈJÊÊCBˆÃBˆ˜[Y\ËY
ÜšY›İËÙ[ÖÚWK•˜[YOË•Ôİš[™Ê
HÏÈˆŠNÃBˆCBˆÜš]\‹•Üš]S[™Jİš[™Ë’›Ú[Š‹‹˜[Y\ÊJNÃBˆCBƒBˆÙ\HH˜[ÙNÃBˆ\]T]X™[

NÃBˆ\P]]Ô™\Ú^™UÚ]Y[[ÜJ
NÃBˆØ\İ][Ë”ÚİÕØ\İ
Ü›Ûİ¹/çykf9¢$9b§ÈŠNÃBˆCBˆØ]Ú
^Ù\[Ûˆ^
CBˆÃBˆY\ÜØYÙP›Ş”ÚİÊ	¹-ŞZÙZK‹JS¢¶W‚äÖW76vWÒ"Â.™IŠ¼ˆ°5•ÍÍ…•	½á	ÕÑÑ½¹Ì¹=,°5•ÍÍ…•	½á%½¸¹ÉÉ½È¤ì4(€€€€€€€€€€€ô4(€€€€€€€ô4(4(€€€€€€€ÁÉ¥Ù…Ñ”Ù½¥M…Ù•ÍÙQ½A…Ñ ¡ÍÑÉ¥¹œÁ…Ñ ¤4(€€€€€€€ì4(€€€€€€€€€€€}É¥ü¹¹‘‘¥Ğ ¤ì4(4(€€€€€€€€€€€€¼¼ƒ’şw–¶c’âèUQ´àİ¥Ñ 	=7¾ò3†»’ştá•°ƒ¢÷š¶†»¢†åˆ«ä¸­æ–‡ç¼–ç 
            using var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(true));

            // ä» _grid è¯»å–è¡¨å¤´
            var headers = new List<string>();
            foreach (DataGridViewColumn col in _grid.Columns)
                headers.Add(col.Name);
            writer.WriteLine(string.Join(",", headers));

            // ä» _grid è¯»å–æ•°æ®è¡Œ
            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                if (gridRow.IsNewRow) continue;
                var values = new List<string

NÃBˆ›Üˆ
[HHÈHÙÜšYÛÛ[[œËÛİ[ÈJÊÊCBˆÃBˆ˜[Y\ËY
ÜšY›İËÙ[ÖÚWK•˜[YOË•Ôİš[™Ê
HÏÈˆŠNÃBˆCBˆÜš]\‹•Üš]S[™Jİš[™Ë’›Ú[Š‹‹˜[Y\ÊJNÃBˆCBˆCBƒBˆš]˜]H›ÚY\]QÜšY

CBˆÃBˆYˆ
ÙÜšYOH[
H™]\›ÃBƒBˆÙÜšY”İ\Ü[™^[İ]

NÃBˆÙÜšYÛÛ[[œËÛX\Š
NÃBˆÙÜšY”›İÜËÛX\Š
NÃBƒBˆ›Ü™XXÚ
˜\ˆÛÛ[ˆØÛÛ[[œÊCBˆÃBˆÙÜšYÛÛ[[œËY
™]È]QÜšYšY]Õ^›ŞÛÛ[[ƒBˆÃBˆXY\•^HÛÛBˆ˜[YHHÛÛBˆZ[š[][UÚYHLBˆÛÜ[ÙHH]QÜšYšY]ĞÛÛ[[”ÛÜ[ÙK“›İÛÜX›CBˆJNÃBˆCBƒBˆ›Ü™XXÚ
˜\ˆ›İÈ[ˆÜ›İÜÊCBˆÃBˆYˆ
›İËÛİ[OH
HÛÛ[YNÃBˆ˜\ˆÙ[ÈH™]È\İØš™XİŠ
NÃBˆ›Üˆ
[HHÈHØÛÛ[[œËÛİ[ÈJÊÊCBˆÃBˆÙ[ËY
H›İËÛİ[È›İÖÚWHˆˆŠNÃBˆCBˆÙÜšY”›İÜËY
Ù[Ë•Ğ\œ˜^J
JNÃBˆCBƒBˆÙÜšY”™\İ[YS^[İ]

NÃBƒBˆ\P]]Ô™\Ú^™UÚ]Y[[ÜJ
NÃBˆCBƒBˆš]˜]H›ÚY\]T]X™[

CBˆÃBˆYˆ
Ù\SX™[OH[
CBˆÃBˆÙ\SX™[•^HÙ\HÈˆ
ˆ9§*¹/çykfˆˆˆ8§$È9mì¹/çykfÃBˆCBˆCBƒBˆš]˜]H›ÚYX\šÑ\J
CBˆÃBˆÙ\HHYNÃBˆ\]T]X™[

NÃBˆCBƒBˆËËÈİ[[X\OƒBˆËËÈ:#­ùan[Ù>X˜Şh™>[Èy¨B55bih~K»n‹zş[èNûÈyJK¨î˜:{Û.i{nŠû¾XùnûÈĞ¢òòòÂ÷7VÖÖ'“àĞ¢V&Æ–27G&–ær7W'&VçD77eF‚Óâö7W'&VçEFƒ°Ğ Ğ¢òòòÇ7VÖÖ'“àĞ¢òòòK¸âöw&–BŠû–>[–öE‰æ‰€æœ‰æ•°æ®è¡Œï"9å*9.£¹/çykf9d£9kï9aî»ÈĞ¢òòòÂ÷7VÖÖ'“àĞ¢&—fFRÆ—7CÄÆ—7CÇ7G&–æpø•ÑÉ¥‘…Ñ„ ¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÉ•ÍÕ±Ğ€ô¹•Ü1¥ÍĞñ1¥ÍĞñÍÑÉ¥¹œø();
            if (_grid == null) return result;

            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                if (gridRow.IsNewRow) continue;
                var row = new List<string>();
                for (int i = 0; i < _grid.Columns.Count; i++)
                {
                    row.Add(gridRow.Cells[i].Value?.ToString() ?? "");
                }
                result.Add(row);
            }
            return result;
        }

        /// <summaryBˆËËÈ:#­ùcå¹odùbcyï%º/¤yæ¡ÔÕˆ9¥l9£k»ÈyJK¨î˜:{Û.i{ny»Nhê^Šû–>[––¶cšVÃš6»¼‰
        /// ä»¥ DataGridView ä¸ºå”¯ä¸€æ•°æ®æºï¼Œå¼ºåˆ¶æäº¤å½“å‰ç¼–è¾‘å¹¶è¿‡æ»¤å…¨ç©ºè¡Œ
        /// </summary>
        public List<Dictionary<string, string>Ù]İ\œ™[]J
CBˆÃBˆ˜\ˆ™\İ[H™]È\İXİ[Û˜\Oİš[™Ëİš[™Ãâ‚“°Ğ¢–b…öw&–BÓÒçVÆÂ’&WGW&â&W7VÇC°Ğ Ğ¢öw&–BäVæDVF—B‚“°Ğ Ğ¢f÷&V6‚„FFw&–Ef–Wu&÷rw&–E&÷r–âöw&–Bå&÷w2Ğ¢°Ğ¢–b†w&–E&÷rä—4æWu&÷r’6öçF–çVS°Ğ Ğ¢f"F–7BÒæWrF–7F–öæ'“Ç7G&–ærÂ7G&–æsâ‚“°Ğ¢&ööÂ†4ç•fÇVRÒfÇ6S°Ğ Ğ¢f÷"†–çB’Ò²’Âöw&–Bä6öÇVÖç2ä6÷VçC²’²²Ğ¢°Ğ¢7G&–ær¶W’Òöw&–Bä6öÇVÖç5¶•ÒäæÖS°Ğ¢7G&–ærfÇVRÒw&–E&÷rä6VÆÇ5¶•ÒåfÇVSòåFõ7G&–ær‚“òåG&–Ò‚’óò"#°Ğ¢F–7E¶¶W•ÒÒfÇVS°Ğ¢–b‚7G&–ærä—4çVÆÄ÷$V×G’‡fÇVR’’†4ç•fÇVRÒG'VS°Ğ¢ĞĞ Ğ¢òò‹ø~kºNXZz›®ŠÎûÈÎYÎi{n‹ø~kºNX[>™JîZÙ~jë^ûÈ„æÖRô•ûÈK‹®z›®y¨NŠÀĞ¢òò˜şXXÒFFw&–Ef–WrKŠŞKˆŞZèÎi[Ny¨NŠÎŠ*¾[Ù>h‰iÈiXi[hÚîûÈÎZûÎˆ{N‹{>‹ør55bihRîÛ–n{¦ 4(€€€€€€€€€€€€€€€‰½½°¡…Í-•å¥•±‘Ì€ô€…ÍÑÉ¥¹œ¹%Í9Õ±±=ÉµÁÑä¡‘¥Ğ¹•ÑY…±Õ•=É•™…Õ±Ğ ‰9…µ”ˆ¤¤4(€€€€€€€€€€€€€€€€€€€€€€€€€€€€€€€€€˜˜€…ÍÑÉ¥¹œ¹%Í9Õ±±=ÉµÁÑä¡‘¥Ğ¹•ÑY…±Õ•=É•™…Õ±Ğ ‰%@ˆ¤¤ì4(€€€€€€€€€€€€€€€¥˜€¡¡…Í¹åY…±Õ”€˜˜¡…Í-•å¥•±‘Ì¤É•ÍÕ±Ğ¹‘¡‘¥Ğ¤ì4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€É•ÑÕÉ¸É•ÍÕ±Ğì4(€€€€€€€ô4(4(€€€€€€€€¼¼¼€ñÍÕµµ…Éäø4(€€€€€€€€¼¼¼ƒª_–#å…³é—­æ—¶è°ƒç”¨ï¼Œç”¨äºæ¸…ç†èµ„æº
        /// </summaryBˆX›XÈ›ÚYÛÛÜÚ[™Ê
CBˆÃBˆËÈ9ê¥XúÉ—­æ—¶çš„æ¸…ç†é€º)Ğ¢ĞĞ¢ĞĞ§Ğ