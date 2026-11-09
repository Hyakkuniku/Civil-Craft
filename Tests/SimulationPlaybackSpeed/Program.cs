using System;

int checks = 0;
void Check(bool value, string message)
{
    checks++;
    if (!value) throw new InvalidOperationException(message);
}
void Near(float actual, float expected, string message)
{
    Check(Math.Abs(actual - expected) <= Math.Max(0.00001f, Math.Abs(expected) * 0.00001f),
        message + $" (expected {expected}, actual {actual})");
}

var state = new SimulationPlaybackState();
Check(SimulationPlaybackState.MinimumStep == -2 && SimulationPlaybackState.MaximumStep == 3,
    "Playback exposes the quarter-speed through three-times-speed bounds.");
Check(!state.Active && state.Step == 0, "Idle state starts neutral.");
Near(state.Scale, 1f, "Idle playback cannot hold the game in slow motion.");
Check(!state.TryStep(1), "An idle arrow does not change playback.");
var firstLesson = new object();
state.SetLessonMultiplier(firstLesson, 0.5f);
state.Begin(1f);
Near(state.Scale, 1f, "A stale lesson submitted outside a simulation cannot leak into its next attempt.");

Check(state.TryStep(-1) && state.Step == -1, "First slower click selects its adjacent notch.");
Near(state.Scale, 0.5f, "First slower notch halves simulation speed.");
Check(state.TryStep(-1) && state.Step == -2, "Second slower click reaches the slowest notch.");
Near(state.Scale, 0.25f, "Slowest notch runs at quarter speed.");
Check(!state.TryStep(-1) && !state.TryStep(int.MinValue), "Slower clicks cannot exceed the slowest bound.");
foreach (float expected in new[] { 0.5f, 1f, 1.5f, 2f, 3f })
{
    Check(state.TryStep(1), "Each faster click advances one notch.");
    Near(state.Scale, expected, "Playback advances in the intended order.");
}
Check(state.Step == SimulationPlaybackState.MaximumStep, "Three-times speed is the last notch.");
Check(!state.TryStep(1) && !state.TryStep(int.MaxValue), "Faster clicks cannot exceed three-times speed.");
Check(!state.TryStep(0), "A zero direction changes nothing.");
Check(state.TryStep(int.MinValue) && state.Step == 2, "Any negative direction moves exactly one notch safely.");
Near(state.Scale, 2f, "Slowing from three-times speed reaches double speed.");
foreach (float expected in new[] { 1.5f, 1f, 0.5f, 0.25f })
{
    Check(state.TryStep(-1), "Each slower click retreats one notch.");
    Near(state.Scale, expected, "Playback descends in the intended order.");
}
Check(state.Step == SimulationPlaybackState.MinimumStep && !state.TryStep(-1),
    "Descending the complete range still clamps at quarter speed.");

state.Begin(1f);
for (int step = 0; step < SimulationPlaybackState.MaximumStep; step++) state.TryStep(1);
state.SetLessonMultiplier(firstLesson, 0.5f);
Near(state.Scale, 1.5f, "Authored lesson slowdown composes with three-times playback.");
Check(state.ClearLessonMultiplier(firstLesson), "A lesson opened at three-times speed releases its owner.");
Near(state.Scale, 3f, "Closing a lesson restores the selected three-times playback.");

state.Begin(0.5f);
Check(state.Active && state.Step == 0, "A new attempt resets the user's notch.");
Near(state.Scale, 0.5f, "An authored half-speed baseline remains half speed at neutral.");
state.TryStep(1);
Near(state.Scale, 0.75f, "Playback scales the authored baseline rather than replacing it.");
state.TryStep(1);
Near(state.Scale, 1f, "Double playback of an authored half-speed attempt is normal wall-clock speed.");

state.Begin(1f);
state.SetLessonMultiplier(firstLesson, 0.5f);
Near(state.Scale, 0.5f, "Opening a lesson applies its authored speed.");
state.TryStep(1);
Near(state.Scale, 0.75f, "The selected playback notch composes with an open lesson.");
state.SetLessonMultiplier(firstLesson, 0.5f);
Near(state.Scale, 0.75f, "Updating the same lesson does not compound its slowdown.");
state.SetLessonMultiplier(firstLesson, 0.25f);
Near(state.Scale, 0.375f, "Changing an owner's authored value replaces its existing factor.");
Check(state.ClearLessonMultiplier(firstLesson), "Closing a lesson releases its owned factor.");
Near(state.Scale, 1.5f, "Closing the lesson preserves the user's chosen speed.");
Check(!state.ClearLessonMultiplier(firstLesson), "Duplicate lesson closure is harmless.");

var equalOwnerA = new EqualOwner();
var equalOwnerB = new EqualOwner();
state.SetLessonMultiplier(equalOwnerA, 0.5f);
state.SetLessonMultiplier(equalOwnerB, 0.5f);
Near(state.LessonMultiplier, 0.25f, "Distinct owners remain distinct even when they override equality.");
Check(state.ClearLessonMultiplier(equalOwnerA), "One owner releases only its own slowdown.");
Near(state.Scale, 0.75f, "The remaining owner's slowdown stays active.");
state.SetLessonMultiplier(equalOwnerA, 0.1f);
Near(state.LessonMultiplier, 0.1f, "Combined lesson slowdown is bounded at ten percent.");
state.ClearLessonMultiplier(equalOwnerB);
state.SetLessonMultiplier(equalOwnerA, 0.01f);
Near(state.LessonMultiplier, 0.1f, "Very small authored factors are safely bounded.");
state.SetLessonMultiplier(equalOwnerA, 2f);
Near(state.LessonMultiplier, 1f, "A lesson cannot unexpectedly fast-forward the attempt.");
state.SetLessonMultiplier(null, 0.1f);
Check(!state.ClearLessonMultiplier(null), "A missing owner is harmless.");

foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
{
    state.SetLessonMultiplier(equalOwnerA, invalid);
    Near(state.LessonMultiplier, 1f, "Invalid lesson speed falls back to neutral.");
    state.Begin(invalid);
    Near(state.Scale, 1f, "Invalid baseline cannot freeze or reverse the simulation.");
}

state.Begin(float.MaxValue);
for (int step = 0; step < SimulationPlaybackState.MaximumStep; step++) state.TryStep(1);
Check(float.IsFinite(state.Scale) && state.Scale > 0f, "Extreme valid baselines cannot overflow the requested clock scale.");
state.Begin(float.Epsilon);
state.SetLessonMultiplier(firstLesson, 0.1f);
state.TryStep(-1); state.TryStep(-1);
Check(float.IsFinite(state.Scale) && state.Scale > 0f, "A tiny positive baseline cannot underflow to a paused clock.");

state.End();
state.End();
Check(!state.Active && state.Step == 0, "Cancellation and teardown are safe to repeat.");
Near(state.Scale, 1f, "Ending an attempt returns playback to neutral.");
Near(state.LessonMultiplier, 1f, "Ending an attempt releases every lesson owner.");
Check(!state.ClearLessonMultiplier(firstLesson), "Teardown leaves no stale owner leases.");
state.Begin(0.75f);
Near(state.Scale, 0.75f, "Retry takes its own baseline without retaining the last attempt's speed.");
Check(state.Step == 0, "Retry starts at the neutral notch.");

Console.WriteLine($"PASS: {checks} simulation playback assertions.");

sealed class EqualOwner
{
    public override bool Equals(object other) => other is EqualOwner;
    public override int GetHashCode() => 1;
}
