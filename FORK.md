# Codari's fork of FishNet

This is [FirstGearGames/FishNet](https://github.com/FirstGearGames/FishNet) with a small set of
changes, made so it installs as a UPM git package with no UGUI and beside Netcode for GameObjects.
Nothing here is sent upstream. Everything that differs from upstream is listed below; whoever merges
the next upstream version keeps each of these.

Install it from a git URL pinned to a commit:

```json
"com.firstgeargames.fishnet": "https://github.com/Codari/FishNet.git?path=Assets/FishNet#<commit>"
```

## What differs from upstream

### 1. No UGUI or TextMeshPro

FishNet's networking core never uses UGUI. These did, and are deleted:

- `Assets/FishNet/Demos/` and `Demos.meta`, the whole folder: the `FishNet.Demos` assembly uses UGUI
  in its scripts, prefabs and scenes.
- Four GameKit utilities that use UGUI and that nothing in FishNet references, under
  `Assets/FishNet/Runtime/Plugins/GameKit/Dependencies/Utilities/`:
  `LayoutGroups.cs`, `Types/ScrollbarValueSetter.cs`, `Types/PointerMonoBehaviour.cs` and
  `Types/Canvases/FloatingImage.cs`.
- Two TextMeshPro ones in the same place, which compiled only under `#if TEXTMESHPRO`:
  `Types/Canvases/OptionButton.cs` and `Types/Canvases/ImageOptionButton.cs`.
- Each with its `.meta`.

And two edits:

- `Runtime/Plugins/GameKit/Dependencies/GameKit.Dependencies.asmdef` no longer references
  `GUID:6055be8ebefd69e48b49212b09b47b2f` (`Unity.TextMeshPro`), and its `TEXTMESHPRO`
  `versionDefines` entry is gone.
- `Assets/FishNet/package.json` no longer depends on `com.unity.ugui`. This one matters most: a
  git package installs its dependencies, so with the line in place UPM brings ugui back.

What stays: `Canvas`, `CanvasGroup` and `RectTransform` (the built-in `com.unity.modules.ui`
module, not the ugui package), and the IMGUI `OnGUI` code.

### 2. Three codegen helpers have GUIDs of their own

FishNet's `CodeGenerating/ILCore/` copied three files from Netcode for GameObjects together with
their `.meta` files, so each had the same GUID as an NGO file:

| File | Upstream GUID (= NGO's) | Ours |
|---|---|---|
| `FNPostProcessorAssemblyResolver.cs` | `2c247f4266b2864eb96e6a9ae6557d31` | `fe7c230653e21779d8a064a025dc1e51` |
| `FNPostProcessorReflectionImporter.cs` | `484e8ad8c4dde382ea67036b32935ef1` | `69be0c491ad3600157042f8c3b6de5f5` |
| `FNPostProcessorReflectionImporterProvider.cs` | `f9273a5dad109ab0783891e36c983080` | `5a004e273d38aa626e8d325d07d8f8d0` |

Under `Assets/`, Unity quietly gives one of two clashing files a new GUID. A git package is
read-only, so with NGO installed Unity keeps NGO's files and ignores FishNet's ("The asset will be
ignored"), and `Unity.FishNet.Codegen` fails with `CS0246` in `ILCoreHelper.cs`. They are plain
editor-only codegen classes that nothing serializes a reference to, so new GUIDs break nothing.
Needed only while NGO is in the same project, harmless otherwise.

### 3. No Mirror upgrade tool

`Assets/FishNet/Upgrading/` (`MirrorUpgrade.cs`, `UpgradeFromMirrorMenu.cs`) is deleted. It has no
asmdef, so inside a package Unity reports that it "will not be compiled", and nothing references it.

### 4. A scene outside Build Settings is sent by its name

`Runtime/Managing/Scened/SceneLookupData.cs`, the `SceneLookupData(Scene)` constructor. Upstream
takes the name from `SceneUtility.GetScenePathByBuildIndex(scene.buildIndex)`, which is empty for a
scene outside Build Settings (Addressables, a content directory, a scene loaded by path), so
clients were asked to load `''`. When that path is empty it uses `scene.name`, which a custom scene
processor can map to the scene it loads.

### 5. A copy of a placed object loses its SceneId when it is spawned, not when it wakes

Upstream 4.7.3 cleared `SceneId` in `NetworkObject.Awake` whenever `gameObject.scene.buildIndex == -1`,
meant for an `Instantiate` copy of a placed object (FishNet #1034). That also caught every object
placed in a scene outside Build Settings: it lost its id before FishNet scanned the scene, and was
never spawned. The decision moves to `Spawn`, where FishNet has already scanned every loaded scene:

- `Runtime/Object/NetworkObject/NetworkObject.cs`, `Awake`: the clear is deleted.
- `Runtime/Object/NetworkObject/NetworkObject.Serialized.cs`: a `[NonSerialized] FoundInSceneScan`
  flag. An `Instantiate` copy never inherits it.
- `Runtime/Utility/Extension/Scenes.cs`, `GetSceneNetworkObjects`: at run time, sets the flag on each
  object it returns that has a `SceneId`. FishNet's own scans (a scene loading, the server starting,
  a client authenticating) all go through it.
- `Runtime/Managing/Server/Object/ServerObjects.cs`, `Spawn`: `ClearCopiedSceneIds` unsets the
  `SceneId` of each `NetworkObject` in the spawned hierarchy that has one but no flag.

A copy that already exists when a scan runs is flagged like a placed object. In the same scene as
its original, the scan reports the duplicate `SceneId` as an error.

### 6. A client paces its tick to drain its replicate queue on the server (`4.7.4+3`)

With `_dropExcessiveReplicates` on, the server runs one of a client's queued replicates a tick and the
client sends one a tick, so a queue that a burst of late packets filled never drains: the client's
input delay stays at its worst burst so far, up to `_maximumServerReplicates` + 1 ticks. Upstream's
timing update aligns the client's tick with the server's and never reads the queue. Now the server
reports the queue's standing depth in the timing update, and the client lengthens its next second of
ticks just enough to send the excess fewer replicates. No input is lost, and the server still runs
one replicate a tick. Off by default (`_replicateQueuePacing` 0), and then nothing behaves
differently except the extra byte and the version string. (Codari/ProjectOmega #459, option 4; OMG-976.)

- `Runtime/Managing/NetworkManager.cs`: `FISHNET_VERSION` is `"4.7.4+3"`. The timing update's wire
  changed, and FishNet's handshake refuses a peer whose string differs, so a `+2` peer is turned away
  at connect instead of misreading every timing update. **A change to the wire bumps this string**
  along with `package.json` (§ Versions). FishNet's own editor code reads only the part before the
  first `.` (`ScriptingDefines`) or compares it whole (the prefab generator's rebuild check).
- `Runtime/Managing/Prediction/PredictionManager.cs`:
  - Two client settings, `_replicateQueueTarget` (2) and `_replicateQueuePacing` (0 to 5, in
    percent; `MAXIMUM_REPLICATE_QUEUE_PACING`), with `Get`/`Set` methods for each, and
    `GetReplicateQueuePace`, which works out the pace from them. It is public and static, so a project
    can test it.
  - **`ReconcileToStates`, Appended order, while paced:** the client reconciles the newest state that
    can be reconciled, and disposes of the older ones before it. While paced, the server sends its
    states at the server's rate and the client reconciles one per tick of its slower clock, so without
    this the backlog grows by the ticks drained, up to `RemoveExcessiveStates`' cap, and stands once
    the pacing ends: corrections reaching the owner that much later for the rest of the session. The
    newest state is the server's state after the older ones, so the replay from it covers whatever
    they corrected. This is chosen over dropping one state per tick drained: it removes only what a
    newer state supersedes, keeps no count to fall out of step with the states actually sent (a
    server sends none for an object that has stopped changing), and collapses to one state a tick
    as Inserted order does, but keeps the newest, whereas Inserted keeps the oldest. Its limit,
    shared with Inserted order and the cap: with several predicted objects, one whose last resends
    sit only in the disposed states misses that correction until it changes again (an object that
    has stopped changing sends no further state).
    Not paced, nothing changes.
- `Runtime/Managing/Prediction/Editor/PredictionManagerEditor.cs`: draws both settings under Client.
- `Runtime/Object/NetworkBehaviour/NetworkBehaviour.Prediction.cs`, `Replicate_NonAuthoritative`: on
  the server, after each tick's replicate, what is still queued is sampled on the owner
  (`NetworkConnection.SampleReplicateQueue`). A client host's own objects are not sampled.
- `Runtime/Connection/NetworkConnection.Prediction.cs`: the fewest queued since the last timing update,
  **the minimum over every replicate of every object the connection owns**, so one queue that runs dry
  keeps the client from pacing at all. Each update takes it (`TakeReplicateQueueFloor`) and keeps it as
  `ReplicateQueueFloor`, 0 when nothing was sampled. That is the delay that stood the whole interval,
  which jitter did not use.
- `Runtime/Managing/Timing/TimeManager.cs`:
  - **The wire.** `TimingUpdate` carries one more byte, the floor, after the tick, so it is now 7
    bytes.
  - **The pace.** On a client, `ParseTimingUpdate` keeps the floor (`ReplicateQueueFloor`, public)
    and sets a pace: the excess past the target spread over the next `TimingTickInterval` ticks,
    capped at the pacing percentage. `PacedTickDelta`, the adjusted tick delta lengthened by that
    pace, replaces `_adjustedTickDelta` in `UpdateNormalizedDeltaTime`, `IncreaseTick` and
    `GetPreciseTick`; `TickDelta`, the step every tick simulates, is untouched. The pace ends with its
    last tick (`CountPacedTick`), so a lost update cannot leave a client paced
    (`IsPacingReplicateQueue` while it runs).
  - **Reordering.** A timing update whose packet's server tick is not newer than the last one the
    pacing applied does not pace: it reports an older interval, by a floor already drained past.
    The alignment below still reads it as upstream does.
  - **The alignment.** A timing update after a paced tick does not nudge `_adjustedTickDelta`, since
    the ticks the client fell behind are the pacing's own. It still sets `Tick`, and still resets
    past a difference of 4. A whole interval at the 5 % cap falls 2.86 ticks behind, and the
    difference is a whole number with a tick of noise either way, so **the cap leaves no room for
    noise**: on a jittery link a paced second can read as 4 and past, and FishNet then resets the
    adjusted delta to `TickDelta`, as it does for any extreme difference. Harmless: the pace is kept
    apart from the adjusted delta, and the alignment nudges back from nominal as it always has.
  - **A client that stops** forgets the pace and the last report (`ResetReplicateQueuePacing`).

## Branches

- `main`, the default branch: released versions, the commits consuming projects pin.
- `dev`: changes being prepared for the next `+N`, merged into `main` when released.
- `upstream`: FirstGearGames' `main`, exactly as they publish it. Nothing of ours is ever committed
  to it, so it only moves by fast-forward, and `git diff upstream main` shows everything this file
  lists.

## Versions

`package.json` carries upstream's version plus `+N`, our build of that version: `4.7.4+1`,
`4.7.4+2`, then `4.7.5+1` after merging upstream's 4.7.5. `+N` is SemVer build metadata ("the same
version, built differently"), so it does not read as a pre-release. Each release is tagged with
the same string.

`NetworkManager.FISHNET_VERSION` carries the same string from `4.7.4+3` on (§ 6): FishNet refuses a
peer whose string differs, so peers of two builds whose wire differs are refused at connect.

UPM decides what to install by the commit in `packages-lock.json`, not by this version, so a
consuming project updates by changing the `#<commit>` in its manifest. Pin the hash rather than the
tag: a tag can be moved, a hash cannot.

## Merging a new upstream version

Do not use GitHub's **Sync fork** button: it merges upstream straight into `main` with nothing
checked, and the version is not reset.

1. Once per clone: `git remote add fishnet https://github.com/FirstGearGames/FishNet.git` and
   `git config remote.fishnet.tagOpt --no-tags`. The remote is `fishnet`, not `upstream`, so that
   `upstream` names only the branch. It fetches no tags because FishNet's are not ours to publish:
   their `4.7.4` tag is on a first 4.7.4R commit (`a3627529`) that contained FishNet Pro code, which
   they replaced on `main` (`a2e2c3e1`) without moving the tag. Never push their tags here.
2. Bring `upstream` level with theirs: `git fetch fishnet`, then
   `git push origin fishnet/main:refs/heads/upstream`. Git refuses that push unless it is a
   fast-forward, so a commit made on `upstream` by mistake shows up here rather than being merged.
3. Branch off `main`, e.g. `update/4.7.5`, and merge `upstream`.
4. Settle the conflicts. Most are modify/delete on the files deleted above: keep them deleted
   (`git rm`). If upstream changed one of the three `FNPostProcessor*.cs.meta` files, keep our GUID.
5. Look for new UGUI or TextMeshPro uses, and a `com.unity.ugui` line back in `package.json`:
   `git grep -n -e "UnityEngine.UI" -e "UnityEngine.EventSystems" -e "TMPro" -e "com.unity.ugui"`.
   Delete or strip what turns up and add it to this file.
6. Set `package.json` to the new upstream version with `+1`, and `FISHNET_VERSION`
   (`Runtime/Managing/NetworkManager.cs`) to the same string: the merge brings upstream's back.
7. Merge into `main`, tag the commit with the version, and push `main` and the tag. Merge `main`
   into `dev` too, so the next changes start from the new version.
8. In each consuming project, re-pin the manifest to the new commit hash, open the editor and check
   that it compiles with no ugui in `packages-lock.json`.
