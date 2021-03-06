namespace DeOps.Services.Transfer
{
    partial class TransferView
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            DeOps.Interface.TLVex.ToggleColumnHeader toggleColumnHeader1 = new DeOps.Interface.TLVex.ToggleColumnHeader();
            DeOps.Interface.TLVex.ToggleColumnHeader toggleColumnHeader2 = new DeOps.Interface.TLVex.ToggleColumnHeader();
            this.FastTimer = new System.Windows.Forms.Timer(this.components);
            this.TransferList = new DeOps.Interface.TLVex.TreeListViewEx();
            this.ShowDownloads = new System.Windows.Forms.CheckBox();
            this.ShowUploads = new System.Windows.Forms.CheckBox();
            this.ShowPending = new System.Windows.Forms.CheckBox();
            this.ShowPartials = new System.Windows.Forms.CheckBox();
            this.ExpandLink = new System.Windows.Forms.LinkLabel();
            this.CollapseLink = new System.Windows.Forms.LinkLabel();
            this.SuspendLayout();
            // 
            // FastTimer
            // 
            this.FastTimer.Enabled = true;
            this.FastTimer.Interval = 250;
            this.FastTimer.Tick += new System.EventHandler(this.FastTimer_Tick);
            // 
            // TransferList
            // 
            this.TransferList.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.TransferList.BackColor = System.Drawing.SystemColors.Window;
            toggleColumnHeader1.Hovered = false;
            toggleColumnHeader1.Image = null;
            toggleColumnHeader1.Index = 0;
            toggleColumnHeader1.Pressed = false;
            toggleColumnHeader1.ScaleStyle = DeOps.Interface.TLVex.ColumnScaleStyle.Spring;
            toggleColumnHeader1.Selected = false;
            toggleColumnHeader1.Text = "Details";
            toggleColumnHeader1.TextAlign = System.Windows.Forms.HorizontalAlignment.Left;
            toggleColumnHeader1.Visible = true;
            toggleColumnHeader1.Width = 414;
            toggleColumnHeader2.Hovered = false;
            toggleColumnHeader2.Image = null;
            toggleColumnHeader2.Index = 0;
            toggleColumnHeader2.Pressed = false;
            toggleColumnHeader2.ScaleStyle = DeOps.Interface.TLVex.ColumnScaleStyle.Slide;
            toggleColumnHeader2.Selected = false;
            toggleColumnHeader2.Text = "Bitfield";
            toggleColumnHeader2.TextAlign = System.Windows.Forms.HorizontalAlignment.Left;
            toggleColumnHeader2.Visible = true;
            toggleColumnHeader2.Width = 200;
            this.TransferList.Columns.AddRange(new DeOps.Interface.TLVex.ToggleColumnHeader[] {
            toggleColumnHeader1,
            toggleColumnHeader2});
            this.TransferList.ColumnSortColor = System.Drawing.Color.Gainsboro;
            this.TransferList.ColumnTrackColor = System.Drawing.Color.WhiteSmoke;
            this.TransferList.GridLineColor = System.Drawing.Color.WhiteSmoke;
            this.TransferList.HeaderMenu = null;
            this.TransferList.ItemHeight = 20;
            this.TransferList.ItemMenu = null;
            this.TransferList.LabelEdit = false;
            this.TransferList.Location = new System.Drawing.Point(12, 38);
            this.TransferList.Name = "TransferList";
            this.TransferList.RowSelectColor = System.Drawing.SystemColors.Highlight;
            this.TransferList.RowTrackColor = System.Drawing.Color.WhiteSmoke;
            this.TransferList.Size = new System.Drawing.Size(618, 230);
            this.TransferList.SmallImageList = null;
            this.TransferList.StateImageList = null;
            this.TransferList.TabIndex = 1;
            this.TransferList.Text = "treeListViewEx2";
            this.TransferList.MouseClick += new System.Windows.Forms.MouseEventHandler(this.TransferList_MouseClick);
            // 
            // ShowDownloads
            // 
            this.ShowDownloads.AutoSize = true;
            this.ShowDownloads.Checked = true;
            this.ShowDownloads.CheckState = System.Windows.Forms.CheckState.Checked;
            this.ShowDownloads.Location = new System.Drawing.Point(12, 12);
            this.ShowDownloads.Name = "ShowDownloads";
            this.ShowDownloads.Size = new System.Drawing.Size(79, 17);
            this.ShowDownloads.TabIndex = 2;
            this.ShowDownloads.Text = "Downloads";
            this.ShowDownloads.UseVisualStyleBackColor = true;
            this.ShowDownloads.CheckedChanged += new System.EventHandler(this.DownloadsCheck_CheckedChanged);
            // 
            // ShowUploads
            // 
            this.ShowUploads.AutoSize = true;
            this.ShowUploads.Checked = true;
            this.ShowUploads.CheckState = System.Windows.Forms.CheckState.Checked;
            this.ShowUploads.Location = new System.Drawing.Point(97, 12);
            this.ShowUploads.Name = "ShowUploads";
            this.ShowUploads.Size = new System.Drawing.Size(65, 17);
            this.ShowUploads.TabIndex = 3;
            this.ShowUploads.Text = "Uploads";
            this.ShowUploads.UseVisualStyleBackColor = true;
            this.ShowUploads.CheckedChanged += new System.EventHandler(this.UploadsCheck_CheckedChanged);
            // 
            // ShowPending
            // 
            this.ShowPending.AutoSize = true;
            this.ShowPending.Location = new System.Drawing.Point(168, 12);
            this.ShowPending.Name = "ShowPending";
            this.ShowPending.Size = new System.Drawing.Size(65, 17);
            this.ShowPending.TabIndex = 4;
            this.ShowPending.Text = "Pending";
            this.ShowPending.UseVisualStyleBackColor = true;
            this.ShowPending.CheckedChanged += new System.EventHandler(this.PendingCheck_CheckedChanged);
            // 
            // ShowPartials
            // 
            this.ShowPartials.AutoSize = true;
            this.ShowPartials.Location = new System.Drawing.Point(239, 12);
            this.ShowPartials.Name = "ShowPartials";
            this.ShowPartials.Size = new System.Drawing.Size(60, 17);
            this.ShowPartials.TabIndex = 5;
            this.ShowPartials.Text = "Partials";
            this.ShowPartials.UseVisualStyleBackColor = true;
            this.ShowPartials.CheckedChanged += new System.EventHandler(this.PartialsCheck_CheckedChanged);
            // 
            // ExpandLink
            // 
            this.ExpandLink.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.ExpandLink.AutoSize = true;
            this.ExpandLink.Location = new System.Drawing.Point(506, 16);
            this.ExpandLink.Name = "ExpandLink";
            this.ExpandLink.Size = new System.Drawing.Size(57, 13);
            this.ExpandLink.TabIndex = 6;
            this.ExpandLink.TabStop = true;
            this.ExpandLink.Text = "Expand All";
            this.ExpandLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.ExpandLink_LinkClicked);
            // 
            // CollapseLink
            // 
            this.CollapseLink.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.CollapseLink.AutoSize = true;
            this.CollapseLink.Location = new System.Drawing.Point(569, 16);
            this.CollapseLink.Name = "CollapseLink";
            this.CollapseLink.Size = new System.Drawing.Size(61, 13);
            this.CollapseLink.TabIndex = 7;
            this.CollapseLink.TabStop = true;
            this.CollapseLink.Text = "Collapse All";
            this.CollapseLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.CollapseLink_LinkClicked);
            // 
            // TransferView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.WhiteSmoke;
            this.ClientSize = new System.Drawing.Size(642, 280);
            this.Controls.Add(this.CollapseLink);
            this.Controls.Add(this.ExpandLink);
            this.Controls.Add(this.ShowPartials);
            this.Controls.Add(this.ShowPending);
            this.Controls.Add(this.ShowUploads);
            this.Controls.Add(this.ShowDownloads);
            this.Controls.Add(this.TransferList);
            this.Name = "TransferView";
            this.Text = "Transfers";
            this.Load += new System.EventHandler(this.TransferView_Load);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.TransferView_FormClosing);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Timer FastTimer;
        private DeOps.Interface.TLVex.TreeListViewEx TransferList;
        private System.Windows.Forms.CheckBox ShowDownloads;
        private System.Windows.Forms.CheckBox ShowUploads;
        private System.Windows.Forms.CheckBox ShowPending;
        private System.Windows.Forms.CheckBox ShowPartials;
        private System.Windows.Forms.LinkLabel ExpandLink;
        private System.Windows.Forms.LinkLabel CollapseLink;
    }
}