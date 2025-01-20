using MediaInfo;
using FFMpegCore;
using System.Drawing.Imaging;

namespace MediaImporter
{
    public partial class MainForm : Form
    {
        private TreeView folderTreeView = null!;
        private Button importButton = null!;
        private string? selectedPath;
        private ListView detailsListView = null!;
        private PictureBox previewBox = null!;

        public MainForm()
        {
            InitializeComponent();
            InitializeUI();
        }

        private void InitializeUI()
        {
            // Create and configure TreeView
            folderTreeView = new TreeView
            {
                CheckBoxes = true,
                Dock = DockStyle.Left,
                Width = 300
            };

            // Create import button
            importButton = new Button
            {
                Text = "Import Selected Files",
                Dock = DockStyle.Bottom,
                Height = 30
            };

            // Create preview PictureBox
            previewBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };

            // Create and configure ListView for metadata
            detailsListView = new ListView
            {
                View = View.Details,
                Dock = DockStyle.Fill,
                FullRowSelect = true
            };

            detailsListView.Columns.Add("Property", 150);
            detailsListView.Columns.Add("Value", 300);

            // Create main horizontal split between tree and right panel
            SplitContainer mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal
            };

            // Create vertical split for right side (preview and details)
            SplitContainer rightSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical
            };

            Controls.Add(mainSplit);
            mainSplit.Panel1.Controls.Add(folderTreeView);
            mainSplit.Panel2.Controls.Add(rightSplit);

            rightSplit.Panel1.Controls.Add(previewBox);
            rightSplit.Panel2.Controls.Add(detailsListView);

            // Wire up events
            folderTreeView.AfterCheck += FolderTreeView_AfterCheck;
            importButton.Click += ImportButton_Click;
            folderTreeView.AfterSelect += FolderTreeView_AfterSelect;

            // Add "Browse" button
            Button browseButton = new Button
            {
                Text = "Browse for Drive",
                Dock = DockStyle.Top,
                Height = 30
            };
            browseButton.Click += BrowseButton_Click;
            Controls.Add(browseButton);
        }

        private void BrowseButton_Click(object? sender, EventArgs e)
        {
            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    selectedPath = folderDialog.SelectedPath;
                    PopulateTreeView(selectedPath);
                }
            }
        }

        private void PopulateTreeView(string path)
        {
            folderTreeView.Nodes.Clear();
            TreeNode rootNode = new TreeNode(Path.GetFileName(path));
            rootNode.Tag = path;
            folderTreeView.Nodes.Add(rootNode);
            PopulateDirectoryNodes(rootNode);
            rootNode.Expand();
        }

        private void PopulateDirectoryNodes(TreeNode parentNode)
        {
            try
            {
                string path = (string)parentNode.Tag;

                // Add files
                string[] files = Directory.GetFiles(path, "*.*")
                    .Where(file => IsImageOrVideo(file))
                    .ToArray();

                foreach (string file in files)
                {
                    TreeNode fileNode = new TreeNode(Path.GetFileName(file));
                    fileNode.Tag = file;
                    parentNode.Nodes.Add(fileNode);
                }

                // Add subdirectories
                string[] directories = Directory.GetDirectories(path);
                foreach (string directory in directories)
                {
                    try
                    {
                        TreeNode directoryNode = new TreeNode(Path.GetFileName(directory));
                        directoryNode.Tag = directory;
                        parentNode.Nodes.Add(directoryNode);
                        PopulateDirectoryNodes(directoryNode);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip inaccessible directories
                        continue;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip inaccessible directories
                return;
            }
        }

        private bool IsImageOrVideo(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            string[] validExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp",
                                   ".mp4", ".mov", ".avi", ".wmv" };
            return validExtensions.Contains(extension);
        }

        private void FolderTreeView_AfterCheck(object? sender, TreeViewEventArgs e)
        {
            // Recursively check/uncheck child nodes
            foreach (TreeNode child in e.Node.Nodes)
            {
                child.Checked = e.Node.Checked;
            }
        }

        private async void FolderTreeView_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            if (e.Node == null) return;
            string path = (string)e.Node.Tag;
            
            if (File.Exists(path))
            {
                ShowFileMetadata(path);
                await Task.Run(() => ShowPreview(path));
            }
        }

        private void ShowFileMetadata(string filePath)
        {
            detailsListView.Items.Clear();
            
            try
            {
                // Basic file information
                var fileInfo = new FileInfo(filePath);
                
                // Check if file is online-only
                if ((fileInfo.Attributes & FileAttributes.Offline) == FileAttributes.Offline)
                {
                    AddMetadataItem("Status", "File is online-only. Download it first to view metadata.");
                    AddMetadataItem("File Name", fileInfo.Name);
                    return;
                }

                AddMetadataItem("File Name", fileInfo.Name);
                AddMetadataItem("Size", $"{fileInfo.Length / 1024.0:N2} KB");
                AddMetadataItem("Created", fileInfo.CreationTime.ToString());
                AddMetadataItem("Modified", fileInfo.LastWriteTime.ToString());

                string extension = Path.GetExtension(filePath).ToLower();
                
                if (IsVideo(extension))
                {
                    ShowVideoMetadata(filePath);
                }
                else if (IsImage(extension))
                {
                    ShowImageMetadata(filePath);
                }
            }
            catch (Exception ex)
            {
                AddMetadataItem("Error", ex.Message);
            }
        }

        private void ShowVideoMetadata(string filePath)
        {
            try
            {
                var mediaInfo = new MediaInfo.MediaInfo();
                if (mediaInfo == null)
                {
                    AddMetadataItem("Error", "Could not initialize MediaInfo");
                    return;
                }

                var result = mediaInfo.Open(filePath);
                if (result == 0)
                {
                    AddMetadataItem("Error", "Could not open file with MediaInfo");
                    return;
                }

                // Enable detailed output
                mediaInfo.Option("Complete", "1");
                mediaInfo.Option("Inform", "Text");
                
                // General information
                AddMetadataItem("Format", mediaInfo.Get(StreamKind.General, 0, "Format"));
                AddMetadataItem("Duration", mediaInfo.Get(StreamKind.General, 0, "Duration/String3"));
                AddMetadataItem("Overall Bitrate", mediaInfo.Get(StreamKind.General, 0, "OverallBitRate/String"));
                AddMetadataItem("File Size", mediaInfo.Get(StreamKind.General, 0, "FileSize/String"));
                
                // Video track information
                AddMetadataItem("Video Codec", mediaInfo.Get(StreamKind.Video, 0, "Format"));
                AddMetadataItem("Frame Rate", mediaInfo.Get(StreamKind.Video, 0, "FrameRate"));
                AddMetadataItem("Frame Count", mediaInfo.Get(StreamKind.Video, 0, "FrameCount"));
                AddMetadataItem("Resolution", $"{mediaInfo.Get(StreamKind.Video, 0, "Width")}x{mediaInfo.Get(StreamKind.Video, 0, "Height")}");
                AddMetadataItem("Bit Depth", mediaInfo.Get(StreamKind.Video, 0, "BitDepth"));
                AddMetadataItem("Color Space", mediaInfo.Get(StreamKind.Video, 0, "ColorSpace"));
                AddMetadataItem("Chroma Subsampling", mediaInfo.Get(StreamKind.Video, 0, "ChromaSubsampling"));
                AddMetadataItem("Video Bitrate", mediaInfo.Get(StreamKind.Video, 0, "BitRate/String"));

                // Add raw output for debugging
                AddMetadataItem("Raw Info", mediaInfo.Inform());

                mediaInfo.Close();
            }
            catch (Exception ex)
            {
                AddMetadataItem("MediaInfo Error", ex.Message);
                if (ex.InnerException != null)
                {
                    AddMetadataItem("Inner Error", ex.InnerException.Message);
                }
            }
        }

        private void ShowImageMetadata(string filePath)
        {
            using (var image = Image.FromFile(filePath))
            {
                AddMetadataItem("Dimensions", $"{image.Width} x {image.Height}");
                AddMetadataItem("Resolution", $"{image.HorizontalResolution} x {image.VerticalResolution} DPI");
                AddMetadataItem("Pixel Format", image.PixelFormat.ToString());

                // Read EXIF data
                foreach (var prop in image.PropertyItems)
                {
                    try
                    {
                        string propName = prop.Id.ToString("X");
                        string propValue = System.Text.Encoding.ASCII.GetString(prop.Value).Trim('\0');
                        if (!string.IsNullOrWhiteSpace(propValue))
                        {
                            AddMetadataItem($"EXIF {propName}", propValue);
                        }
                    }
                    catch
                    {
                        // Skip problematic EXIF data
                    }
                }
            }
        }

        private void AddMetadataItem(string property, string value)
        {
            if (detailsListView.InvokeRequired)
            {
                detailsListView.Invoke(() => AddMetadataItem(property, value));
                return;
            }

            var item = new ListViewItem(property);
            item.SubItems.Add(value);
            detailsListView.Items.Add(item);
        }

        private bool IsVideo(string extension)
        {
            return new[] { ".mp4", ".mov", ".avi", ".wmv" }.Contains(extension);
        }

        private bool IsImage(string extension)
        {
            return new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" }.Contains(extension);
        }

        private void ImportButton_Click(object? sender, EventArgs e)
        {
            List<string> selectedFiles = new List<string>();
            GetCheckedFiles(folderTreeView.Nodes, selectedFiles);

            if (selectedFiles.Count > 0)
            {
                using (FolderBrowserDialog destinationDialog = new FolderBrowserDialog())
                {
                    destinationDialog.Description = "Select destination folder for import";
                    if (destinationDialog.ShowDialog() == DialogResult.OK)
                    {
                        ImportFiles(selectedFiles, destinationDialog.SelectedPath);
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select files to import.", "No Files Selected",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void GetCheckedFiles(TreeNodeCollection nodes, List<string> selectedFiles)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Checked)
                {
                    string path = (string)node.Tag;
                    if (File.Exists(path)) // If it's a file
                    {
                        selectedFiles.Add(path);
                    }
                }
                GetCheckedFiles(node.Nodes, selectedFiles);
            }
        }

        private void ImportFiles(List<string> files, string destinationPath)
        {
            try
            {
                foreach (string file in files)
                {
                    string fileName = Path.GetFileName(file);
                    string destinationFile = Path.Combine(destinationPath, fileName);
                    File.Copy(file, destinationFile, true);
                }
                MessageBox.Show("Files imported successfully!", "Import Complete",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error importing files: {ex.Message}", "Import Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void ShowPreview(string filePath)
        {
            try
            {
                if ((new FileInfo(filePath).Attributes & FileAttributes.Offline) == FileAttributes.Offline)
                {
                    previewBox.Image = null;
                    return;
                }

                string extension = Path.GetExtension(filePath).ToLower();
                if (IsImage(extension))
                {
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    {
                        previewBox.Image?.Dispose();
                        previewBox.Image = Image.FromStream(stream);
                    }
                }
                else if (IsVideo(extension))
                {
                    try
                    {
                        // Extract frame from video
                        string tempFile = Path.GetTempFileName() + ".jpg";
                        var success = await Task.Run(() => FFMpeg.Snapshot(filePath, tempFile));
                        if (success)
                        {
                            previewBox.Image?.Dispose();
                            previewBox.Image = Image.FromFile(tempFile);
                            File.Delete(tempFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        AddMetadataItem("Preview Error", ex.Message);
                        previewBox.Image = null;
                    }
                }
                else
                {
                    previewBox.Image?.Dispose();
                    previewBox.Image = null;
                }
            }
            catch (Exception ex)
            {
                AddMetadataItem("Preview Error", ex.Message);
                previewBox.Image?.Dispose();
                previewBox.Image = null;
            }
        }
    }
}
