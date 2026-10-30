# PlayForge Proving Grounds

The test project for [PlayForge](../PlayForge). It consumes `PlayForge/Assets/PlayForge` as a local UPM package,
so every test runs against the canonical source and the packaging gets tested along the way. Its roadmap is
`Docs/REFACTOR_PLAN.md` in the PlayForge repo, and that plan's Environment section has the setup details.

## Running the tests

Close the editor for this project first; batch mode can't share it. Paths below are relative to this folder.

```bash
"/c/Program Files/Unity/Hub/Editor/6000.0.33f1/Editor/Unity.exe" -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults results.xml -logFile test.log
```

```bash
python Tools/test-report.py results.xml
```

Exit code 0 means everything passed and 2 means tests failed. A run takes about 10 seconds. In the editor, use
Window → General → Test Runner → PlayMode.

**The green gate.** A test for a bug not fixed yet carries the `Red` category, so a full run stays red until the bug
is fixed. The report lists those apart from new failures. To check nothing else broke, leave them out:

```bash
"/c/Program Files/Unity/Hub/Editor/6000.0.33f1/Editor/Unity.exe" -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testCategory "!Red" -testResults results.xml -logFile test.log
```

When a fix turns a red test green, the report says so, and the test loses its category in the same commit.

**The planted failures** check the harness itself. Every test in that category must fail, each for its own reason,
so exit code 2 is the right answer here and the report is what counts. Run this after changing the harness:

```bash
"/c/Program Files/Unity/Hub/Editor/6000.0.33f1/Editor/Unity.exe" -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testCategory Planted -testResults planted.xml -logFile planted.log
```

```bash
python Tools/check-planted.py planted.xml
```

## The suites

| Folder | What's in it |
|---|---|
| `Tests/Harness` | The harness's own tests |
| `Tests/Golden` | The 25 golden scenarios, ported from PlayForge's custom runner. Each runs on both GAS hubs where it applies |
| `Tests/Framework` | Framework behaviour by area, with tests for a bug named after its tracker ID. Each red test has been shown to go green when its bug is fixed |
| `Tests/Planted` | Tests that must fail, proving the harness's checks work |

## Writing a test

Derive from `ProvingGroundsTest` and put the body in `Live`. Each test gets a fresh `World`: a real ProcessControl and
GameRoot, brought up by the real Bootstrapper and stepped by Unity's player loop.

```csharp
public class ExampleTests : ProvingGroundsTest
{
    [UnityTest]
    public IEnumerator PC_01_A_throwing_step_stalls_nothing() => Live(async () =>
    {
        var broken = World.Probe("Broken", EProcessLifecycle.Synchronous);
        var after = World.Probe("After", EProcessLifecycle.Synchronous);
        Faults.Arm(broken.Recorder.Site(ProbeHook.Update));

        await Frames.Step(5);

        Assert.That(after.Recorder[ProbeHook.Update], Is.EqualTo(5));
    });
}
```

Name a test after its tracker ID when it pins a bug, and give it `[Category("Red")]` until the bug is fixed. The base
class checks the invariants after every test, so a test about something else still catches a leak.

`[Values] Hub hub` with `World.Entity(hub, …)` runs a test on both GAS hubs, and `Frames.SetFrameRate(fps)` runs it at
another fixed rate.

**The one rule:** never call `WhenUpdate` or `WhenLateUpdate` by hand. Step frames and let ProcessControl do it.
Hand-stepping is how the golden tests hid GAS-1.

## The harness

| Part | Use it through | What it does |
|---|---|---|
| Frame stepper | `Frames` | Runs whole frames: `Step`, `Seconds`, `Until`, `ExactlyIn`, `Holds`. Each frame lasts exactly 1/60 s and runs one FixedUpdate |
| Scene fixtures | `World`, `Make` | Spawns probes, templates, entities and scenes, builds assets in code, and clears ProcessControl, tags and registries between tests |
| Invariants | Automatic | No leaked control blocks, every Prepare has one Clean, attribution slices sum to ActiveValue, dealt equals received |
| Fault injector | `Faults` | Makes any probe hook, task, worker or callback throw. A fault that fires must be logged exactly once, or the test fails |
| Allocation probe | `new AllocationProbe()` | Bytes per operation and per frame. A clean frame reads exactly 0 |
| State oracle | `ProcessStateOracle` | The 96 transitions ProcessControl means to make, written out, and a driver that checks a cell against a live process |

**Probes** are the instruments. `ProbeProcess` and `ProbeMonoProcess` count their hooks, and `ProbeSequenceTask`,
`ProbeAbilityTask` and the `Probe…Worker` classes report to the ledger or the fault injector.

## What the batch environment does differently

- `WaitForEndOfFrame` never resumes under `-nographics`. Use `Frames`.
- Time is captured, but **unscaled time isn't**: it stays on the wall clock, so anything timed on it can't be stepped
  and takes almost no game time. 600 frames take about 50 ms.
- The GC API returns 0 under Mono. Use `AllocationProbe`.
- Allocation in a coroutine isn't measured, because the test runner's own coroutine tick is subtracted with it.
  PlayForge's stepping and UniTask continuations are measured.
- `MeasureFrames` retakes a measure that isn't clean, up to 3 times, because a rare burst of about 1 KB lands at
  random, even with nothing spawned. A real leak fails every attempt.
