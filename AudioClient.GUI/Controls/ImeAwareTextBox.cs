using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Metadata;

namespace AudioClient.GUI.Controls;

public class ImeAwareTextBox : TextBox
{
    private TextPresenter? _textPresenter;

    [AssignBinding]
    protected override Type StyleKeyOverride => typeof(TextBox);

    public bool HasActiveImeComposition => !string.IsNullOrEmpty(_textPresenter?.PreeditText);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _textPresenter = e.NameScope.Find<TextPresenter>("PART_TextPresenter");
    }

    public void FlushTextBindingToSource()
    {
        BindingOperations.GetBindingExpressionBase(this, TextProperty)?.UpdateSource();
    }
}
