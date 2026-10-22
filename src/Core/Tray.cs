using System.Drawing;
using System.Windows.Forms;

namespace MishkaHelper.Core;

/// <summary>Tray icon. Left click → quick panel, right click → menu.</summary>
public sealed class Tray : IDisposable
{
    readonly NotifyIcon _icon;

    public Tray()
    {
        var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream;
        _icon = new NotifyIcon
        {
            Icon = new Icon(stream, 32, 32),
            Text = "Mishka Helper",
            Visible = true,
        };

        var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, Font = new Font("Segoe UI", 10f) };
        menu.Items.Add("Открыть", null, (_, _) => App.Current.ShowMain());
        menu.Items.Add("Быстрая панель", null, (_, _) => Actions.Run("quick"));
        menu.Items.Add("Ускорить всё", null, (_, _) => Actions.Run("boost"));
        menu.Items.Add("Виджеты", null, (_, _) => Actions.Run("widgets"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => App.Current.Quit());
        _icon.ContextMenuStrip = menu;

        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Actions.Run("quick"); };
        _icon.MouseDoubleClick += (_, _) => App.Current.ShowMain();
    }

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }

    sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new Colors()) { }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = Color.FromArgb(238, 238, 246); base.OnRenderItemText(e); }

        sealed class Colors : ProfessionalColorTable
        {
            static readonly Color Bg = Color.FromArgb(20, 20, 34), Hi = Color.FromArgb(60, 162, 89, 255);
            public override Color ToolStripDropDownBackground => Bg;
            public override Color MenuBorder => Color.FromArgb(162, 89, 255);
            public override Color MenuItemBorder => Hi;
            public override Color MenuItemSelected => Hi;
            public override Color SeparatorDark => Color.FromArgb(50, 50, 70);
            public override Color SeparatorLight => Bg;
        }
    }
}
