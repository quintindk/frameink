using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace FrameInk;

internal sealed partial class FrameWindow
{
    private const string TextInstructions = "Type to annotate. Ctrl+Enter keeps the text. Esc discards it.";
    private readonly Canvas editorLayer = new();
    private Point? textDrag;
    private TextBox? textEditor;

    private void OpenTextEditor(Point start, Point end)
    {
        var bounds = new Rect(start, end);
        if (bounds.Width < TextAnnotation.MinimumWidth || bounds.Height < TextAnnotation.MinimumHeight)
        {
            ShowStatus("Drag a wider or taller area for text.");
            return;
        }

        double dip = 96d / display.Dpi;
        var draft = ink.BeginText(bounds, selectedColour);
        var edit = new TextBox
        {
            Width = bounds.Width * dip, Height = bounds.Height * dip,
            Padding = new(TextAnnotation.Inset * dip), BorderThickness = new(0), Background = Brushes.Transparent,
            Foreground = selectedColour.Fill, CaretBrush = selectedColour.Fill, FontFamily = TextAnnotation.Family,
            FontSize = TextAnnotation.FontPixels * dip, FontWeight = FontWeights.SemiBold,
            Effect = new DropShadowEffect { Color = draft.OutlineColour, BlurRadius = 3, ShadowDepth = 0, Opacity = 1 },
            TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MaxLength = TextAnnotation.CharacterLimit,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden
        };
        AutomationProperties.SetName(edit, "Annotation text");
        if (surface.Toolbar is { } bar)
        {
            // Keep the floating toolbar clickable if a full-screen textbox crosses its footprint.
            edit.Clip = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new(0, 0, edit.Width, edit.Height)),
                new RectangleGeometry(new((bar.X - crop.X - bounds.X) * dip, (bar.Y - crop.Y - bounds.Y) * dip,
                    bar.Width * dip, bar.Height * dip)));
        }
        edit.ContextMenuOpening += (_, e) => e.Handled = true;
        edit.TextChanged += (_, _) =>
        {
            bool overflow = draft.Overflow;
            draft.SetText(edit.Text);
            if (overflow != draft.Overflow)
                ShowStatus(draft.Overflow ? "Some text is below the box and will not be captured. Shorten it or draw a taller box." : TextInstructions);
        };
        edit.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape && (e.Key != Key.Enter || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))) return;
            e.Handled = true;
            FinishText(e.Key != Key.Escape);
        };
        edit.IsKeyboardFocusWithinChanged += (_, _) =>
        {
            if (textEditor == edit && !edit.IsKeyboardFocusWithin) FinishText(keep: true);
        };

        Canvas.SetLeft(edit, bounds.X * dip);
        Canvas.SetTop(edit, bounds.Y * dip);
        textEditor = edit;
        editorLayer.Children.Add(edit);
        ShowStatus(TextInstructions);
        overlay.Activate();
        edit.Focus();
    }

    private void FinishText(bool keep)
    {
        if (textEditor is not { } edit) return;
        textEditor = null;
        ink.FinishText(keep);
        editorLayer.Children.Remove(edit);
        surface.InvalidateVisual();
        ShowStatus(keep && !string.IsNullOrWhiteSpace(edit.Text) ? "Text added. Use Undo to remove it." : "Text box dismissed.");
    }
}
