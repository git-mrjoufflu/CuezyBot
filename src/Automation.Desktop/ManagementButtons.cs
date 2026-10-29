using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Automation;

namespace Automation.Desktop;

internal static class ManagementButtons
{
    private const string Pencil = "M3,17.25 L3,21 L6.75,21 L17.81,9.94 L14.06,6.19 Z M20.71,7.04 C21.1,6.65 21.1,6.02 20.71,5.63 L18.37,3.29 C17.98,2.9 17.35,2.9 16.96,3.29 L15.13,5.12 L18.88,8.87 Z";
    private const string Trash = "M6,19 C6,20.1 6.9,21 8,21 L16,21 C17.1,21 18,20.1 18,19 L18,7 L6,7 Z M19,4 L15.5,4 L14.5,3 L9.5,3 L8.5,4 L5,4 L5,6 L19,6 Z";
    private const string Disk = "M17,3 L5,3 C3.9,3 3,3.9 3,5 L3,19 C3,20.1 3.9,21 5,21 L19,21 C20.1,21 21,20.1 21,19 L21,7 Z M12,19 C10.34,19 9,17.66 9,16 C9,14.34 10.34,13 12,13 C13.66,13 15,14.34 15,16 C15,17.66 13.66,19 12,19 Z M15,9 L5,9 L5,5 L15,5 Z";
    private const string Undo = "M7.5,8 H16 C18.76,8 21,10.24 21,13 C21,15.76 18.76,18 16,18 H14 V16 H16 C17.66,16 19,14.66 19,13 C19,11.34 17.66,10 16,10 H7.5 L10.75,13.25 L9.33,14.67 L3.66,9 L9.33,3.33 L10.75,4.75 Z";
    private const string Redo = "M16.5,8 H8 C5.24,8 3,10.24 3,13 C3,15.76 5.24,18 8,18 H10 V16 H8 C6.34,16 5,14.66 5,13 C5,11.34 6.34,10 8,10 H16.5 L13.25,13.25 L14.67,14.67 L20.34,9 L14.67,3.33 L13.25,4.75 Z";
    private const string Plus = "M11,2 H13 V11 H22 V13 H13 V22 H11 V13 H2 V11 H11 Z";
    internal static void Add(Button button, bool english)
    {
        button.Content = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(Plus), Fill = Brush.Parse("#E8E9F4"), Width = 20, Height = 20, Stretch = Stretch.Uniform };
        button.Width = 38; button.Height = 38; button.MinWidth = 38; button.MinHeight = 38; button.Padding = new Thickness(0);
        button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        button.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
        var label = english ? "Add" : "Ajouter"; ToolTip.SetTip(button, label); AutomationProperties.SetName(button, label);
    }
    internal static void History(Button button, bool english, bool redo)
    {
        SetIcon(button, redo ? Redo : Undo, "#E8E9F4", redo ? (english ? "Redo" : "Rétablir") : (english ? "Undo" : "Annuler"));
        var label = redo ? (english ? "Redo" : "Rétablir") : (english ? "Undo" : "Annuler"); ToolTip.SetTip(button, label); AutomationProperties.SetName(button, label);
    }
    private static void SetIcon(Button button, string geometry, string color, string label)
    {
        button.Content = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(geometry), Fill = Brush.Parse(color), Width = 20, Height = 20, Stretch = Stretch.Uniform };
        button.MinWidth = 38; button.MinHeight = 38; button.Padding = new Thickness(9);
        ToolTip.SetTip(button, label); AutomationProperties.SetName(button, label);
    }
    internal static Button Edit(bool english)
    {
        var button = new Button(); SetIcon(button, Pencil, "#53D994", english ? "Edit" : "Modifier"); return button;
    }
    internal static Button Delete(bool english)
    {
        var button = new Button(); SetIcon(button, Trash, "#FF5368", english ? "Delete" : "Supprimer"); return button;
    }
    internal static void Save(Button button, bool english) => SetIcon(button, Disk, "#E8E9F4", english ? "Save" : "Enregistrer");
}
