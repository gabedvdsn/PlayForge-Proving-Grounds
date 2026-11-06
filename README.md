# PlayForge Proving Grounds

The test project for [PlayForge](https://github.com/gabedvdsn/PlayForge). It consumes `PlayForge/Assets/PlayForge`
as a local UPM package, from a PlayForge checkout in a folder named `PlayForge` beside this one, so every test runs
against the canonical source and the packaging gets tested along the way. Its roadmap is `Docs/REFACTOR_PLAN.md` in
the PlayForge repo, and that plan's Environment section has the setup details.

## Running the tests

Close the editor for this project first; batch mode can't share it. Paths below are relative to this folder.

```bash
"/c/Program Files/Unity/Hub/Editor/6000.0.33f1/Editor/Unity.exe" -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults results.xml -logFile test.log
```

```bash
python Tools/test-report.py results.xml
```

Exit code 0 means everything passed and 2 means tests failed. A PlayMode run takes about three minutes. In the
editor, use Window → General → Test Runner → PlayMode.

The editor tests, for PlayForge's editor code and for the framework outside play mode, are EditMode tests. Run them
the same way, with `-testPlatform EditMode`:

```bash
"/c/Program Files/Unity/Hub/Editor/6000.0.33f1/Editor/Unity.exe" -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults editmode.xml -logFile editmode.log
```

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

## Continuous integration

`.github/workflows/gates.yml` runs the full gates on GitHub Actions: the PlayMode suite in three orders (as written,
and seeds 1729 and 90210), the planted failures, which `Tools/check-planted.py` checks, and the EditMode suite. Each
job checks PlayForge out beside this project, where the package reference expects it, and runs Unity 6000.0.33f1 in
GameCI's container. It runs on a push to `main`, on a pull request, by hand (Actions → Gates → Run workflow, with the
PlayForge commit to test), each day, and on a push to PlayForge's `main` when PlayForge's repository holds a token
for it.

The jobs need a Unity license. Until the repository's secrets hold one, they're skipped, with a warning saying so. In
Settings → Secrets and variables → Actions, add:

| Secret | What it holds |
|---|---|
| `UNITY_EMAIL` | The Unity account's email |
| `UNITY_PASSWORD` | The account's password |
| `UNITY_LICENSE` | For Unity Personal: the text of the license file the Hub writes as it activates, `C:\ProgramData\Unity\Unity_lic.ulf` on Windows |
| `UNITY_SERIAL` | For Unity Pro or Plus, in place of `UNITY_LICENSE`: the serial number |

For each push to PlayForge's `main` to run the gates, add `PROVING_GROUNDS_DISPATCH_TOKEN` to PlayForge's own secrets:
a fine-grained personal access token for this repository, with Contents read and write. PlayForge's
`.github/workflows/proving-grounds.yml` sends it. Without it, the daily run tests PlayForge's latest `main`.

## The suites

| Folder | What's in it |
|---|---|
| `Tests/Harness` | The harness's own tests |
| `Tests/Golden` | The 25 golden scenarios, ported from PlayForge's custom runner. Each runs on both GAS hubs where it applies |
| `Tests/Framework` | Framework behaviour by area, with tests for a bug named after its tracker ID. Each red test has been shown to go green when its bug is fixed |
| `Tests/Planted` | Tests that must fail, proving the harness's checks work |
| `Tests/Labs` | The gameplay labs, category Lab: small games built in code, each played by a bot. Each has a scene in `Scenes/Labs` that plays its rounds slowly enough to watch |
| `Tests/Stretch` | The review's stretch scenarios, category Stretch, which push past ordinary use: rollback, possession, a headless simulation and more |
| `Tests/Stress` | The stress suite, category Stress: PlayForge at a busy game's scale, such as 500 entities with 20 effects each and an area ability over 200 targets; processes registered and ended by the ten thousand; and soaks, the same work again and again, for what it leaves behind. A cost is compared per entity or process with the same run at a tenth of the size, so the machine's speed doesn't decide it. Half an hour of bullet heaven is Explicit, as it runs for minutes: run it with `-testCategory Soak` |
| `Tests/Editor` | EditMode tests: PlayForge's editor code, and the framework driven outside play mode, as editor tools drive it. Some of them change EditorPrefs, which every project on the machine shares, and put back what they change. `DomainReloadTests` plays two sessions with domain reloading off. `EditorSmokeTests` is Phase 4's gate, below |

## The editor smoke suite

`EditorSmokeTests` is Phase 4's gate (the UI interview's Q36). It runs once in each editor theme, Dark and Light, and
checks two things.

- **Every inspector and window opens without an error.** Each inspector opens on a new asset of its type, on two of
  them selected together, and on each of the 82 assets in the Demos sample's Data folders. The sample's `Samples~`
  folder isn't imported, so the suite copies those assets into `Assets/PlayForge Smoke Demos` while it runs and
  deletes the folder after. Each window opens the way the editor opens it: The Forge, the Runtime Debugger, every
  import window and every dialog.
- **Every edit in an inspector undoes.** The sweep changes each value an inspector shows, one at a time, as a user
  would: a text, number, toggle, popup or enum field. Then one Undo must put the asset back exactly as it was, byte
  for byte in its serialized form. It sweeps a new asset of each type, a MonoProcess, and the fullest Demo asset in
  each Data folder.

Before it checks an inspector, the suite opens every collapsed part, so it reaches what's inside: the inspector's
sections, each drawer's collapsed parts and every foldout. It shows a section without clicking its header, since the
click would save the section's state as yours. Two checks fail if a new inspector or window isn't in the suite, and
three prove the suite itself works: an IMGUI window's `OnGUI` runs, every list draws a row for each element, and an
edit that records no Undo is reported.

**The theme** is switched through Unity's own setting, which saves it as your choice, machine-wide, in the
`UserSkin` EditorPrefs key. Each fixture puts that key back as it found it, unset or not. If a run is killed in the
Light fixture, your editor may open in the Light theme once; set it back under Preferences → General → Editor Theme.

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
`ProbeAbilityTask` and the `Probe…Worker` classes report to the ledger or the fault injector. `ProbeShot` is a
homing shot that lands its effects on whatever it's aimed at, for the disjoint tests.

## What the batch environment does differently

- `WaitForEndOfFrame` never resumes under `-nographics`. Use `Frames`.
- Time is captured, but **unscaled time isn't**: it stays on the wall clock, so anything timed on it can't be stepped
  and takes almost no game time. 600 frames take about 50 ms.
- The GC API returns 0 under Mono. Use `AllocationProbe`.
- Allocation in a coroutine isn't measured, because the test runner's own coroutine tick is subtracted with it.
  PlayForge's stepping and UniTask continuations are measured.
- `MeasureFrames` retakes a measure that isn't clean, up to 3 times, because a rare burst of about 1 KB lands at
  random, even with nothing spawned. A real leak fails every attempt.
- **No editor window can be drawn** under `-nographics`: opening one logs that no graphics device is available. The
  window still opens, in an editor panel, and its `CreateGUI` runs. Nothing repaints it, so an IMGUI window's
  `OnGUI` never runs on its own; `EditorSmokeTests` hands its view a layout event instead. A UI Toolkit field sends
  its change and focus events only through a panel, so an editor test that needs them adds the element to a
  `UIDocument`'s runtime panel, which works there (`TheForgeSettingsTests`, `AssetTagTests`). A button answers a
  `NavigationSubmitEvent` sent to it. A test that only reads a window, such as its title, can open it and set
  `LogAssert.ignoreFailingMessages` around the call (`MenuTests`).
- **Bindings run in that panel too.** Add the whole inspector root to it, as `InspectorUndoTests` does, and wait a few
  frames for the bindings before reading a field or after an Undo. Put the root in a `ScrollView`, as the Inspector
  does, when the test needs a list's elements: squeezed into the panel's 640 by 480, a list draws only some rows.
- **A multiple selection needs objects without DontSave flags.** A SerializedObject over several objects flagged
  `HideAndDontSave` writes only to the first of them, so a multi-editing test uses plain scene objects and destroys
  them itself.
- **A list's elements may not draw there** without that `ScrollView`. Inside the Attribute Set inspector, the
  elements' scaler drawers never ran in the test panel, so `ScalerDrawerTests` draws one element's property with a
  `PropertyField` of its own, bound to the asset's SerializedObject.
- **A dialog would block** an interactive run, so code under test asks through a hook a test can answer, such as
  `ForgeAssetDeletion.Confirm`.
- **A second play session** in one editor can be played from an EditMode `[UnityTest]`: turn domain reloading off in
  `EditorSettings`, then yield `new EnterPlayMode(false)` and `new ExitPlayMode()` twice (`DomainReloadTests`).
