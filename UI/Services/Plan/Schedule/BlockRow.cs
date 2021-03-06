using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Data;
using System.Text;
using System.Windows.Forms;

using DeOps.Implementation;
using DeOps.Services.Trust;
using DeOps.Services.Plan;
using DeOps.Interface;
using DeOps.Interface.Views;
using DeOps.Interface.TLVex;


namespace DeOps.Services.Plan
{
    public partial class BlockRow : UserControl
    {
        PlanNode Node;
        ScheduleView View;

        ulong UserID;

        Bitmap DisplayBuffer;
        bool Redraw;

        List<BlockArea> BlockAreas = new List<BlockArea>();
        List<BlockArea> GoalAreas = new List<BlockArea>();

        Pen RefPen = new Pen(Color.PowderBlue);
        Pen BigPen = new Pen(Color.FromArgb(224,224,224));
        Pen SmallPen = new Pen(Color.FromArgb(248, 248, 248));
        Pen SelectPen = new Pen(Color.Black, 2);
        SolidBrush Highlight = new SolidBrush(SystemColors.Highlight);
        Pen BlackPen = new Pen(Color.Black);
        Font Tahoma = new Font("Tahoma", 8);

        SolidBrush BlackBrush = new SolidBrush(Color.Black);
        SolidBrush RedBrush = new SolidBrush(Color.Red);
        SolidBrush BlueBrush = new SolidBrush(Color.Blue);

        SolidBrush WhiteBrush = new SolidBrush(Color.White);
        SolidBrush GreenBrush = new SolidBrush(Color.LawnGreen);


        SolidBrush HighMask = new SolidBrush(Color.FromArgb(25, Color.Red));
        SolidBrush LowMask = new SolidBrush(Color.FromArgb(25, Color.Blue));
        SolidBrush NeutralMask = new SolidBrush(Color.FromArgb(25, Color.Black));

        DateTime StartTime;
        DateTime EndTime;
        long TicksperPixel;

        List<ulong> Uplinks = new List<ulong>();


        public BlockRow()
        {
            InitializeComponent();
            BlackPen.DashStyle = DashStyle.Dot;
        }

        public BlockRow(PlanNode node)
        {
            InitializeComponent();
            
            Node = node;
            View = node.View;
            UserID = node.Link.UserID;

            SetStyle(ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);

            BlackPen.DashStyle = DashStyle.Dot;
        }

        Font debugFont = new Font("Tahoma", 8);
        SolidBrush blackBrush = new SolidBrush(Color.Black);
       
        private void BlockRow_Paint(object sender, PaintEventArgs e)
        {
            if(DisplayBuffer == null )
                DisplayBuffer = new Bitmap(Width, Height);

            if (!Redraw)
            {
                e.Graphics.DrawImage(DisplayBuffer, 0, 0);
                return;
            }
            Redraw = false;

            // background
            Graphics buffer = Graphics.FromImage(DisplayBuffer);

            buffer.Clear(Color.White);
            //buffer.SmoothingMode = SmoothingMode.AntiAlias;


            // draw tick lines
            foreach (int mark in View.ScheduleSlider.SmallMarks)
               buffer.DrawLine(SmallPen, mark, 0, mark, Height);

            foreach (int mark in View.ScheduleSlider.BigMarks)
               buffer.DrawLine(BigPen, mark, 0, mark, Height);

           //buffer.DrawLine(RefPen, View.ScheduleSlider.RefMark, 0, View.ScheduleSlider.RefMark, Height);

            // setup vars
            Rectangle tempRect = new Rectangle();
            tempRect.Y = 0;
            tempRect.Height = Height;

            StartTime = View.GetStartTime().ToUniversalTime();
            EndTime = View.GetEndTime().ToUniversalTime();
            TicksperPixel = View.ScheduleSlider.TicksperPixel;


            // draw higher plans    
            BlockAreas.Clear();
            GoalAreas.Clear();


            Uplinks = View.Core.Trust.GetUnconfirmedUplinkIDs(UserID, View.ProjectID);

            // upnodes just used for scope calc now
            List<PlanNode> upnodes = new List<PlanNode>();
            PlanNode nextNode = Node;

            while (nextNode.Parent.GetType() == typeof(PlanNode))
            {
                nextNode = (PlanNode) nextNode.Parent;
                upnodes.Add(nextNode);
            }

            upnodes.Reverse();


            // draw plans for each node above in the current block layered on top of one another
            // if we want to change to loop doesnt inherit other loop member's plans, replace foreach uplinks with upnodes
            // not obvious behaviour

            int level = 0;

            //foreach (ulong uplink in Uplinks)
            foreach (PlanNode node in upnodes)
            {
                ulong uplink = node.Link.UserID;

                foreach (PlanBlock block in GetBlocks(uplink))
                    // scope -1 everyone or current level 0 highest + scope is >= than current node
                    if ((block.Scope == -1 || (level + block.Scope >= upnodes.Count))
                        && BlockinRange(block, ref tempRect))
                    {
                        buffer.FillRectangle(GetMask(uplink, true), tempRect);
                        BlockAreas.Add(new BlockArea(tempRect, block, level, false));
                    }

                    level++;
            }

            // draw local plans
            List<List<DrawBlock>> layers = new List<List<DrawBlock>>();

            // arrange visible blocks
            foreach (PlanBlock block in GetBlocks(UserID))
                if(BlockinRange(block, ref tempRect))
                    AddDrawBlock(new DrawBlock(block, tempRect), layers);

            List<KeyValuePair<string, PointF>> StringList = new List<KeyValuePair<string, PointF>>();

            // draw blocks
            if (layers.Count > 0)
            {
                int y = 2;
                int yStep = (Height-2) / layers.Count;

                foreach (List<DrawBlock> list in layers)
                {
                    foreach (DrawBlock item in list)
                        if(item.Above == null)
                        {
                            item.Rect.Y = y;
                            item.Rect.Height = yStep - 1;

                            DrawBlock down = item.Below;
                            while (down != null)
                            {
                                item.Rect.Height += yStep;
                                down = down.Below;
                            }

                            BlockAreas.Add(new BlockArea(item.Rect, item.Block, level, true));

                            Rectangle fill = item.Rect;
                            fill.Height = Height - y;
                            buffer.FillRectangle(GetMask(UserID, true), fill);

                            if (item.Block == View.SelectedBlock)
                                buffer.DrawRectangle(SelectPen, item.Rect);

                            SizeF size = buffer.MeasureString(item.Block.Title, Tahoma);

                            if (size.Width < item.Rect.Width - 2 && size.Height < item.Rect.Height - 2)
                                StringList.Add(new KeyValuePair<string, PointF>(item.Block.Title, 
                                    new PointF(item.Rect.X + (item.Rect.Width - size.Width) / 2, item.Rect.Y + (item.Rect.Height - size.Height) / 2)));
                        }

                    y += yStep;
                }
            }

            // scan higher's goal lists for assigned goals to this id
            if (View.SelectedGoalID != 0)
            {
                // cache what to draw, look at how goals control get progress status
                // color goal bars solid red / blue / gray
                // cache strings, draw after goals

                //Uplinks.Add(Node.Link.DhtID); // add self to scan
                upnodes.Add(Node);

                foreach (PlanNode node in upnodes)
                {
                    ulong userID = node.Link.UserID;
                    OpPlan upPlan = View.Plans.GetPlan(userID, true);

                    if (upPlan != null)
                        if (upPlan.GoalMap.ContainsKey(View.SelectedGoalID))
                            foreach (PlanGoal goal in upPlan.GoalMap[View.SelectedGoalID])
                                if (goal.Project == View.ProjectID && goal.Person == UserID)
                                    if (StartTime < goal.End && goal.End < EndTime)
                                    {
                                        int x = (int)((goal.End.Ticks - StartTime.Ticks) / TicksperPixel);

                                        int completed = 0, total = 0;
                                        View.Plans.GetEstimate(goal, ref completed, ref total);

                                        // draw divider line with little right triangles in top / bottom
                                        buffer.FillRectangle(WhiteBrush, new Rectangle(x - 4, 2, 2, Height - 4));

                                        if (total > 0)
                                        {
                                            int progress = completed * (Height - 4) / total;
                                            buffer.FillRectangle(GreenBrush, new Rectangle(x - 4, 2 + (Height - 4) - progress, 2, progress));
                                        }

                                        buffer.FillPolygon(GetMask(UserID, false), new Point[] {
                                            new Point(x-6,2), 
                                            new Point(x,2),
                                            new Point(x,Height-2),
                                            new Point(x-6,Height-2),
                                            new Point(x-2,Height-2-5),
                                            new Point(x-2,2+5)
                                        });

                                        GoalAreas.Add(new BlockArea(new Rectangle(x - 6, 2, 6, Height - 4), goal));
                                    }
                }
            }

            // draw strings
            foreach (KeyValuePair<string, PointF> pair in StringList)
                buffer.DrawString(pair.Key, Tahoma, blackBrush, pair.Value);

            // draw selection
            if (Node.Selected)
            {
                if (View.PlanStructure.Focused)
                {
                    buffer.FillRectangle(Highlight, 0, 0, Width, 2);
                    buffer.FillRectangle(Highlight, 0, Height - 2, Width, 2);
                }

                else
                {
                    buffer.DrawLine(BlackPen, 1, 0, Width - 1, 0);
                    buffer.DrawLine(BlackPen, 0, Height - 1, Width, Height - 1);
                }
            }

            // Copy buffer to display
            e.Graphics.DrawImage(DisplayBuffer, 0, 0);

        }

        private SolidBrush GetMask(ulong key, bool mask)
        {

            // if key above target - red
            if (View.Uplinks.Contains(key))
                return mask ? HighMask : RedBrush;

            // if target is equal to or above key - blue
            if(key == View.UserID || Uplinks.Contains(View.UserID))
                return mask ? LowMask : BlueBrush;

            // else black
            return mask ? NeutralMask : BlackBrush;
        }

        private List<PlanBlock> GetBlocks(ulong key)
        {
            OpPlan plan = View.Plans.GetPlan(key, true);

            if (plan != null &&
                plan.Loaded &&
                plan.Blocks != null &&
                plan.Blocks.ContainsKey(View.ProjectID))

                return plan.Blocks[View.ProjectID];


            return new List<PlanBlock>();
        }

        private bool BlockinRange(PlanBlock block, ref Rectangle rect)
        {
            bool add = false;

            // start is in span
            if (View.StartTime < block.StartTime && block.StartTime < EndTime)
            {
                rect.X = (int)((block.StartTime.Ticks - StartTime.Ticks) / TicksperPixel);
                rect.Width = Width - rect.X;
                
                add = true;
            }

            // end is in span
            if (StartTime < block.EndTime && block.EndTime < EndTime)
            {
                long startTicks = StartTime.Ticks;

                if (!add)
                    rect.X = 0; // if rect set above, dont reset left
                else
                    startTicks = block.StartTime.Ticks;

                rect.Width = (int)((block.EndTime.Ticks - startTicks) / TicksperPixel);

                add = true;
            }

            // if block spans entire row
            if (block.StartTime < StartTime && EndTime < block.EndTime)
            {
                rect.X = 0;
                rect.Width = Width;
                
                add = true;
            }

            return add;
        }

        private void AddDrawBlock(DrawBlock draw, List<List<DrawBlock>> layers)
        {
            bool conflict = false;

            List<DrawBlock> delete = new List<DrawBlock>();

            foreach (List<DrawBlock> layer in layers)
            {
                conflict = false;
                delete.Clear();

                foreach (DrawBlock item in layer)
                    if (item.Rect.IntersectsWith(draw.Rect))
                    {
                        if (draw.Above != null) // signals block was already drawn, but this layer is full
                            return;

                        if (item.Above == null) // this is the main block for an item, cant draw here, create new layer
                        {
                            conflict = true;
                            break;
                        }
                        else // this item isnt the main so it can be deleted if there is a conflict
                            delete.Add(item);
                        
                    }

                if (!conflict)
                {
                    foreach (DrawBlock item in delete)
                        item.Remove();

                    if (draw.Above != null)
                        draw.Above.Below = draw;

                    draw.Layer = layer;
                    layer.Add(draw);

                    // draw same rect another layer down if there is room
                    DrawBlock next = new DrawBlock(draw.Block, draw.Rect);
                    next.Above = draw;
                    draw = next;
                }
            }

            if (draw.Above != null) // signals block already is drawn in a layer
                return;

            // new layer
            List<DrawBlock> newLayer = new List<DrawBlock>();
            draw.Layer = newLayer;
            newLayer.Add(draw);

            // duplicate previous layer's blocks to this list
            if (layers.Count > 0)
            {
                foreach (DrawBlock topBlock in layers[layers.Count - 1])
                    if (!topBlock.Rect.IntersectsWith(draw.Rect))
                        newLayer.Add(new DrawBlock(newLayer, topBlock));
            }

            layers.Add(newLayer);
        }

        private void BlockRow_Resize(object sender, EventArgs e)
        {
            if (Width > 0 && Height > 0)
            {
                DisplayBuffer = new Bitmap(Width, Height);

                UpdateRow(false);
            }
        }

        public void UpdateRow(bool immediate)
        {
            OpPlan plan = View.Plans.GetPlan(UserID, true);

            if (plan != null && !plan.Loaded)
                View.Plans.LoadPlan(UserID);

            Redraw = true;
            Invalidate();

            if (immediate)
                Update();
        }

        private void BlockRow_VisibleChanged(object sender, EventArgs e)
        {
            UpdateRow(false);
        }

        private void BlockRow_MouseClick(object sender, MouseEventArgs e)
        {
            foreach (BlockArea area in BlockAreas)
                if (area.Local && area.Rect.Contains(e.Location))
                {
                    if (View.SelectedBlock != area.Block)
                    {
                        View.SelectedBlock = area.Block;
                        View.RefreshRows();

                        View.SetDetails(area.Block);
                    }

                    break;
                }


            if (e.Button != MouseButtons.Right)
                return;

            // create details menu

            // if local create edit/delete menu


            ContextMenuStripEx menu = new ContextMenuStripEx();
            ToolStripMenuItem details = new ToolStripMenuItem("Details");

            string indent = "";
            int lastLevel = -1;

            foreach (BlockArea area in BlockAreas)
                if (area.Rect.Contains(e.Location))
                {
                    if (lastLevel == -1)
                        lastLevel = area.Level;

                    if (area.Level > lastLevel)
                    {
                        lastLevel = area.Level;
                        indent += "    ";
                    }

                    details.DropDownItems.Add(new BlockMenuItem(indent + area.Block.Title, area.Block, null, new EventHandler(RClickView)));

                    if (area.Local)
                    {
                        if (UserID != View.Core.UserID)
                            menu.Items.Add(new BlockMenuItem("Details", area.Block, PlanRes.details, new EventHandler(RClickView)));
                        else
                        {
                            menu.Items.Add(new BlockMenuItem("Edit", area.Block, null, new EventHandler(RClickEdit)));
                            menu.Items.Add(new BlockMenuItem("Delete", area.Block, PlanRes.delete, new EventHandler(RClickDelete)));
                        }
                    }
                }

            if (details.DropDownItems.Count > 0)
                menu.Items.Add(details);

            foreach (BlockArea area in GoalAreas)
                if (area.Rect.Contains(e.Location))
                {
                    menu.Items.Add(new BlockMenuItem("View Goal", area.Goal, PlanRes.Goals.ToBitmap(), new EventHandler(RClickGoal)));
                    break;
                }

            if (menu.Items.Count > 0)
                menu.Show(this, e.Location);
        }

        private void RClickView(object sender, EventArgs e)
        {
            BlockMenuItem menu = sender as BlockMenuItem;

            if (menu == null)
                return;

            EditBlock form = new EditBlock(BlockViewMode.Show, View, menu.Block);
            form.ShowDialog(View);
        }

        private void RClickEdit(object sender, EventArgs e)
        {
            BlockMenuItem menu = sender as BlockMenuItem;

            if (menu == null)
                return;

            EditBlock form = new EditBlock(BlockViewMode.Edit, View, menu.Block);

            if (form.ShowDialog(View) == DialogResult.OK)
            {
                View.RefreshRows();
                View.ChangesMade();
            }
        }

        private void RClickDelete(object sender, EventArgs e)
        {
            BlockMenuItem menu = sender as BlockMenuItem;

            if (menu == null)
                return;

            if (View.Plans.LocalPlan.Blocks.ContainsKey(View.ProjectID))
                View.Plans.LocalPlan.Blocks[View.ProjectID].Remove(menu.Block);

            View.RefreshRows();

            View.ChangesMade();
        }

        private void RClickGoal(object sender, EventArgs e)
        {
            BlockMenuItem menu = sender as BlockMenuItem;

            if (menu == null)
                return;

            if (View.External != null && View.UI.GuiMain.GetType() == typeof(MainForm))
                foreach (ExternalView ext in ((MainForm)View.UI.GuiMain).ExternalViews)
                    if(ext.Shell.GetType() == typeof(GoalsView))
                        if (((GoalsView)ext.Shell).UserID == View.UserID && ((GoalsView)ext.Shell).ProjectID == View.ProjectID)
                        {
                            ext.BringToFront();
                            return;
                        }

            // switch to goal view
            GoalsView view = new GoalsView(View.UI, View.Plans, View.UserID, View.ProjectID);
            view.LoadIdent = menu.Goal.Ident;
            view.LoadBranch = menu.Goal.BranchUp;

            view.UI.ShowView(view, view.External != null); 
        }

        private void BlockRow_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            foreach (BlockArea area in BlockAreas)
                if (area.Local && area.Rect.Contains(e.Location))
                {
                    BlockViewMode mode = (UserID == View.Core.UserID) ? BlockViewMode.Edit : BlockViewMode.Show;

                    EditBlock form = new EditBlock(mode, View, area.Block);
                    if (form.ShowDialog(View) == DialogResult.OK && mode == BlockViewMode.Edit)
                        View.ChangesMade();

                    break;
                }
        }

        private void BlockRow_MouseMove(object sender, MouseEventArgs e)
        {
            View.CursorUpdate(this);
        }

        public string GetHoverText()
        {
            Point pos = PointToClient(Cursor.Position);

            bool good = false;
            StringBuilder text = new StringBuilder(100);

            text.Append(View.Core.GetName(Node.Link.UserID));
            //text.Append(" - ");

            //DateTime start = View.StartTime;
            //DateTime time = new DateTime(start.Ticks + View.ScheduleSlider.TicksperPixel * pos.X);
            //text.Append(time.ToString("D"));

            text.Append("\n\n");

            string indent = "";
            int lastLevel = -1;

            foreach (BlockArea area in BlockAreas)
                if (area.Rect.X <= pos.X && pos.X <= area.Rect.X + area.Rect.Width)
                {
                    good = true;

                    if(lastLevel == -1)
                        lastLevel = area.Level;

                    if (area.Level > lastLevel)
                    {
                        lastLevel = area.Level;
                        indent += "    ";
                    }

                    text.Append(indent);
                    text.Append(area.Block.Title);

                    string description = area.Block.Description;

                    if (description != "")
                    {
                        text.Append(" - ");

                        description = description.Replace("\r\n", " ");

                        if (description.Length > 25)
                            text.Append(description.Substring(0, 25) + "...");
                        else
                            text.Append(description);
                    }

                    text.Append("\n");
                }

            foreach (BlockArea area in GoalAreas)
                if (area.Rect.X <= pos.X && pos.X <= area.Rect.X + area.Rect.Width)
                {
                    good = true;

                    text.Append("\nGoal Deadline for\n");
                    text.Append("   " + area.Goal.Title + "\n");
                }

            return good ? text.ToString() : "";
        }
    }

    public class BlockMenuItem : ToolStripMenuItem
    {
        public PlanBlock Block;
        public PlanGoal Goal;

        public BlockMenuItem(string text, PlanGoal goal, Image icon, EventHandler onClick)
            :
            base(text, icon, onClick)
        {
            Goal = goal;
        }

        public BlockMenuItem(string text, PlanBlock block, Image icon, EventHandler onClick)
            :
            base(text, icon, onClick)
        {
            Block = block;
        }
    }


    public class DrawBlock
    {
        public List<DrawBlock> Layer;
        public PlanBlock Block;
        public Rectangle Rect;

        public DrawBlock Above;
        public DrawBlock Below;

        public DrawBlock(PlanBlock block, Rectangle rect)
        {
            Block = block;
            Rect = rect;
        }

        public DrawBlock(List<DrawBlock> layer, DrawBlock top)
        {
            Layer = layer;
            Block = top.Block;
            Rect = top.Rect;

            Above = top;
            top.Below = this;
        }

        public void Remove()
        {
            Layer.Remove(this);

            if (Below != null)
                Below.Remove();
            
            Above.Below = null;
            Above = null;
        }
    }

    public class BlockArea
    {
        public Rectangle Rect;
        public PlanBlock Block;
        public PlanGoal Goal;
        public int       Level;
        public bool      Local;

        public BlockArea(Rectangle rect, PlanGoal goal)
        {
            Rect = rect;
            Goal = goal;
        }

        public BlockArea(Rectangle rect, PlanBlock block, int level, bool local)
        {
            Rect  = rect;
            Block = block;
            Level = level;
            Local = local;
        }
    }
}
