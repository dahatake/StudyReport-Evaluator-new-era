using System.Collections.Immutable;

namespace StudyReportEvaluator.App.Navigation;

public enum WorkflowStep
{
    Input,
    Design,
    Execution,
    Results,
}

public enum WorkflowStepState
{
    Visited,
    Current,
    Upcoming,
}

public sealed record WorkflowStepDefinition(
    WorkflowStep Step,
    int Number,
    string Title,
    string EnglishTitle);

public sealed class WorkflowNavigator
{
    private static readonly ImmutableArray<WorkflowStepDefinition> ClosedSteps =
    [
        new(WorkflowStep.Input, 1, "入力", "INPUT"),
        new(WorkflowStep.Design, 2, "定量化設計", "DESIGN"),
        new(WorkflowStep.Execution, 3, "実行", "EXECUTION"),
        new(WorkflowStep.Results, 4, "結果・出力", "RESULTS"),
    ];

    private int currentIndex;
    private int furthestReachedIndex;

    public const int StepCount = 4;

    public event EventHandler? CurrentStepChanged;

    public ImmutableArray<WorkflowStepDefinition> Steps => ClosedSteps;

    public int CurrentIndex => currentIndex;

    public WorkflowStep CurrentStep => ClosedSteps[currentIndex].Step;

    public WorkflowStepDefinition CurrentDefinition => ClosedSteps[currentIndex];

    public bool CanMovePrevious => currentIndex > 0;

    public bool CanMoveNext => currentIndex < ClosedSteps.Length - 1;

    public bool CanNavigateTo(WorkflowStep step)
    {
        int targetIndex = IndexOf(step);
        return targetIndex <= Math.Min(furthestReachedIndex + 1, ClosedSteps.Length - 1);
    }

    public WorkflowStepState GetState(WorkflowStep step)
    {
        int index = IndexOf(step);
        return index == currentIndex
                ? WorkflowStepState.Current
                : index <= furthestReachedIndex
                    ? WorkflowStepState.Visited
                : WorkflowStepState.Upcoming;
    }

    public bool NavigateTo(WorkflowStep step)
    {
        int targetIndex = IndexOf(step);
        if (!CanNavigateTo(step) || targetIndex == currentIndex)
        {
            return false;
        }

        currentIndex = targetIndex;
        furthestReachedIndex = Math.Max(furthestReachedIndex, currentIndex);
        CurrentStepChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool MovePrevious() =>
        CanMovePrevious && NavigateTo(ClosedSteps[currentIndex - 1].Step);

    public bool MoveNext() =>
        CanMoveNext && NavigateTo(ClosedSteps[currentIndex + 1].Step);

    private static int IndexOf(WorkflowStep step) => step switch
    {
        WorkflowStep.Input => 0,
        WorkflowStep.Design => 1,
        WorkflowStep.Execution => 2,
        WorkflowStep.Results => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Unknown workflow step."),
    };
}