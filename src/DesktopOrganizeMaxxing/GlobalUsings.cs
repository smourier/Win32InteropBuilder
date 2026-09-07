// Global using directives to resolve WPF vs WinForms type ambiguities
// Since UseWindowsForms=true is needed for NotifyIcon, we must explicitly alias conflicting types

global using Application = System.Windows.Application;
global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using Button = System.Windows.Controls.Button;
global using CheckBox = System.Windows.Controls.CheckBox;
global using Clipboard = System.Windows.Clipboard;
global using Color = System.Windows.Media.Color;
global using ColorConverter = System.Windows.Media.ColorConverter;
global using ComboBox = System.Windows.Controls.ComboBox;
global using ContextMenu = System.Windows.Controls.ContextMenu;
global using Cursors = System.Windows.Input.Cursors;
global using FontFamily = System.Windows.Media.FontFamily;
global using Grid = System.Windows.Controls.Grid;
global using Image = System.Windows.Controls.Image;
global using Label = System.Windows.Controls.Label;
global using ListBox = System.Windows.Controls.ListBox;
global using ListView = System.Windows.Controls.ListView;
global using MenuItem = System.Windows.Controls.MenuItem;
global using MessageBox = System.Windows.MessageBox;
global using Orientation = System.Windows.Controls.Orientation;
global using Point = System.Windows.Point;
global using Separator = System.Windows.Controls.Separator;
global using TextBox = System.Windows.Controls.TextBox;
global using ToolTip = System.Windows.Controls.ToolTip;
