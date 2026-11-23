using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LibrarySearch;

class ResultHeaderTests {
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr window, out NativeRect rect);

    struct NativeRect { public int Left, Top, Right, Bottom; }

    static void CheckHeader(ResultList list, int count) {
        list.VirtualListSize = count;
        list.Refresh();
        Application.DoEvents();
        var header = SendMessage(list.Handle, 0x101F, IntPtr.Zero, IntPtr.Zero);
        NativeRect rect;
        if (header == IntPtr.Zero || !GetClientRect(header, out rect) || rect.Bottom < 3)
            throw new Exception("Native header is missing");
        int columnEnd = 0;
        foreach (ColumnHeader column in list.Columns) columnEnd += column.Width;
        if (columnEnd > list.ClientSize.Width)
            throw new Exception("Columns overflow the viewport");
        using (var bitmap = new Bitmap(rect.Right, rect.Bottom)) {
            using (var graphics = Graphics.FromImage(bitmap)) {
                var dc = graphics.GetHdc();
                try { SendMessage(header, 0x0318, dc, new IntPtr(4)); } // WM_PRINTCLIENT
                finally { graphics.ReleaseHdc(dc); }
            }
            int expected = Color.FromArgb(73, 73, 73).ToArgb();
            if (bitmap.GetPixel(3, rect.Bottom / 2).ToArgb() != expected)
                throw new Exception("Header columns were not rendered");
            if (count == 2 && rect.Right - columnEnd < 4)
                throw new Exception("Fixture does not exercise the header gutter");
            for (int x = columnEnd; x < rect.Right; x++)
                if (bitmap.GetPixel(x, rect.Bottom / 2).ToArgb() != expected)
                    throw new Exception("Unthemed header at x=" + x + " rows=" + count);
            Console.WriteLine("PASS width=" + list.Width + " rows=" + count + " gutter=" + (rect.Right - columnEnd));
        }
    }

    [STAThread] static int Main() {
        try {
            Application.EnableVisualStyles();
            using (var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                Location = new Point(-20000, -20000), ClientSize = new Size(300, 170) })
            using (var list = new ResultList { Dock = DockStyle.Fill, View = View.Details, VirtualMode = true,
                BorderStyle = BorderStyle.None, HeaderStyle = ColumnHeaderStyle.Nonclickable }) {
                list.Columns.Add("名称", 120);
                list.Columns.Add("类型", 55);
                list.Columns.Add("编号", 65);
                list.RetrieveVirtualItem += (sender, e) => e.Item = new ListViewItem("PART");
                form.Controls.Add(list);
                form.Show();
                foreach (int width in new[] { 300, 250, 370 }) {
                    form.ClientSize = new Size(width, 170);
                    list.Columns[0].Width = width - 120 - SystemInformation.VerticalScrollBarWidth - 1;
                    foreach (int count in new[] { 2, 80, 2 }) CheckHeader(list, count);
                }
            }
            return 0;
        } catch (Exception error) {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
