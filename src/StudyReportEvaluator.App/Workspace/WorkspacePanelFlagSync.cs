namespace StudyReportEvaluator.App.Workspace;

/// <summary>
/// Keeps a view-model "show this panel" flag (for example the cost toggle) and the selected tab of a
/// workspace group in step. A panel shown in a group of its own is always visible, so the flag only
/// follows the presentation while the panel shares a tab group.
/// </summary>
internal sealed class WorkspacePanelFlagSync(
    PanelWorkspace workspace,
    string panelId,
    Func<bool?> read,
    Action<bool> write)
{
    private bool synchronizing;
    private bool initialPending = true;

    /// <summary>The next presentation applies the flag to the layout first (view attached again).</summary>
    public void Reset() => initialPending = true;

    public void FlagChanged()
    {
        if (synchronizing || read() is not { } value)
        {
            return;
        }

        synchronizing = true;
        try
        {
            if (value)
            {
                workspace.Reveal(panelId);
            }
            else
            {
                workspace.Conceal(panelId);
            }
        }
        finally
        {
            synchronizing = false;
        }

        PresentationChanged();
    }

    public void PresentationChanged()
    {
        if (synchronizing || read() is not { } value)
        {
            return;
        }

        if (initialPending)
        {
            initialPending = false;
            if (value && !workspace.IsPresented(panelId))
            {
                FlagChanged();
                return;
            }
        }

        if (workspace.IsAlone(panelId))
        {
            return;
        }

        bool presented = workspace.IsPresented(panelId);
        if (presented != value)
        {
            synchronizing = true;
            try
            {
                write(presented);
            }
            finally
            {
                synchronizing = false;
            }
        }
    }
}
