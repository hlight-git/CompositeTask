# Composite Task

Hệ thống thực thi task dạng cây cho Unity. Cho phép khai báo chuỗi hành vi (tuần tự hoặc song song), theo dõi tiến trình, và can thiệp runtime (cancel, force complete).

## Mục đích

Composite Task giải quyết bài toán: **mô hình hóa một chuỗi logic phức tạp thành cây khai báo, có thể theo dõi và kiểm soát tại runtime.**

Các use case điển hình: quest/mission system, tutorial flow, cutscene scripting, onboarding sequence, hoặc bất kỳ workflow nào cần chia nhỏ thành các bước con với tiến trình rõ ràng.

Hệ thống được thiết kế theo nguyên tắc: framework chỉ lo **cấu trúc cây + thực thi + progress**, toàn bộ logic cụ thể nằm trong `ITaskDefinition` do user implement. Framework không biết và không cần biết task làm gì.

## Dependencies

- [UniTask](https://github.com/Cysharp/UniTask)
- [Newtonsoft.Json (com.unity.nuget.newtonsoft-json)](https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.0/manual/index.html) — dùng cho JSON import/export
- (Tùy chọn) [Odin Inspector](https://odininspector.com/) — nếu có, Task Tree Editor tự động dùng Odin PropertyTree để vẽ task definition fields

## Cấu trúc

```
Runtime/
  ATaskNode.cs                    — Base class cho mọi node (progress, status, lifecycle)
  CompositeTaskNode.cs            — Node chứa children, chạy Sequential hoặc Parallel
  MonoTaskNode.cs                 — Node lá, wrap một ITaskDefinition
  TaskTree.cs                     — Serializable class giữ root node, entry point để Execute
  ITaskDefinition.cs              — Interface user implement cho logic cụ thể
  TaskDefinitionAttribute.cs      — [TaskDefinition] attribute để đăng ký ITaskDefinition tự động
  IDependencyInjectionVisitor.cs  — Visitor pattern cho dependency injection
  IDependencyInjectionVisitable.cs — Opt-in interface để task definition nhận DI
  ExecutionMode.cs                — Enum: Sequential, Parallel
  TaskNodeStatus.cs               — Enum: Pending, Running, Finishing, Completed, Failed

Editor/
  TaskTreePropertyDrawer.cs       — PropertyDrawer cho TaskTree, vẽ inline trong Inspector (Hierarchy + Inspector panel)
  TaskTreeSerializationBinder.cs  — Whitelist binder cho JSON deserialization
  TaskDefinitionRegistry.cs       — Quét assembly tìm [TaskDefinition] attribute, cache kết quả
  SearchablePopup.cs              — Dropdown popup có search field cho chọn TaskDefinition type
```

## Kiến trúc tổng quan

```
TaskTree (Serializable class, không phải MonoBehaviour)
  └── CompositeTaskNode (Root, Sequential hoặc Parallel)
        ├── MonoTaskNode → ITaskDefinition (logic cụ thể)
        ├── MonoTaskNode → ITaskDefinition
        └── CompositeTaskNode (nested, có thể lồng không giới hạn)
              ├── MonoTaskNode → ITaskDefinition
              └── ...
```

`TaskTree` là một `[Serializable]` class thuần — không phải MonoBehaviour. Nó được nhúng như field trong MonoBehaviour hoặc ScriptableObject tùy theo nhu cầu sử dụng. Root node được khởi tạo sẵn.

Cây chỉ có 2 loại node:

- **CompositeTaskNode** — node cha, chứa children. Mỗi child có `enabled` (bật/tắt), `subTaskValue` (trọng số progress). Chạy children theo `executionMode`: Sequential (lần lượt) hoặc Parallel (đồng thời).
- **MonoTaskNode** — node lá, wrap một `ITaskDefinition`. Toàn bộ logic game nằm ở đây.

## Lifecycle của một node

```
Pending ──Execute()──► Running ──OnRunning──► Finishing ──OnFinishing──► OnCompleted()──► Completed
                         │                                                    ▲
                    ForceComplete()                                           │
                         │                                                    │
                    cancel OnRunning ──► OnFinishing ────────────────────────┘

ForceComplete(immediate: true) ── cancel Running+Finishing ──► OnCompleted() ──► Completed

Exception in OnRunning/OnFinishing ──► Failed (logged via Debug.LogException)
```

Khi `ExecuteAsync()` được gọi:

1. Status chuyển sang `Running`.
2. `OnRunning(ct)` được gọi — đây là nơi logic chính chạy.
3. Khi Running hoàn thành (hoặc bị cancel), status chuyển `Finishing`, `OnFinishing(ct)` được gọi — dùng cho cleanup.
4. Khi Finishing hoàn thành, `OnCompleted()` được gọi — set Progress = 1, Status = Completed, fire event Completed.
5. CancellationTokenSource được dispose trong `finally` block — đảm bảo không leak.

Nếu exception (non-cancellation) xảy ra trong `OnRunning` hoặc `OnFinishing`, exception được log qua `Debug.LogException` và status chuyển sang `Failed`. Trong Parallel mode, child Failed được coi là terminal — không gây infinite hang.

`OnCompleted()` là **điểm hoàn thành duy nhất** — mọi đường đi bình thường đều kết thúc ở đây:

- `ExecuteAsync` hoàn thành bình thường → `OnCompleted()`
- `ForceComplete()` khi Pending → `OnCompleted()` ngay
- `ForceComplete()` khi Running → cancel Running → chờ Finishing → `OnCompleted()`
- `ForceComplete(immediate: true)` → cancel Running + Finishing → `OnCompleted()` ngay

`MonoTaskNode` override `OnCompleted()` để gọi `taskDefinition.OnCompleted()` trước khi gọi `base.OnCompleted()`. Điều này đảm bảo task definition luôn được thông báo khi node hoàn thành, bất kể hoàn thành bằng cách nào.

### Cancellation flow

Khi external cancel xảy ra (từ `CancellationTokenSource` trả về bởi `Execute()`), ATaskNode gọi `CancelAllCancellationTokenSources()` — cancel cả Running lẫn Finishing CTS. CTS được dispose trong `finally` block của `ExecuteAsync`, đảm bảo không có resource leak.

## Progress

Mỗi node có `Progress` (0→1) và `targetProgressToComplete` (mặc định 1.0).

**MonoTaskNode:** `ITaskDefinition` tự set `node.Progress` trong `OnRunning` để báo tiến trình. Progress ở đây chỉ để hiển thị — node hoàn thành khi `OnRunning` + `OnFinishing` chạy xong, không phải khi progress đạt 1.

**CompositeTaskNode:** Progress được tính tự động từ children, có trọng số theo `subTaskValue`. Khi progress đạt `targetProgressToComplete`, composite sẽ auto-complete (gọi `ForceComplete`). Điều này cho phép pattern "hoàn thành 3/5 task phụ là đủ" bằng cách set `targetProgressToComplete = 0.6`.

## Cách dùng

### 1. Implement ITaskDefinition

```csharp
[Serializable]
[TaskDefinition("Wait For Sec")]
public class WaitSecondsTask : ITaskDefinition
{
    public float duration = 1f;

    public async UniTask OnRunning(MonoTaskNode node, CancellationToken ct)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            ct.ThrowIfCancellationRequested();
            elapsed += Time.deltaTime;
            node.Progress = elapsed / duration;
            await UniTask.Yield(ct);
        }
    }

    public UniTask OnFinishing(MonoTaskNode node, CancellationToken ct)
    {
        // Cleanup nếu cần. Được gọi cả khi OnRunning bị cancel qua ForceComplete.
        return UniTask.CompletedTask;
    }

    public void OnCompleted(MonoTaskNode node)
    {
        // Luôn được gọi khi node hoàn thành, bất kể bằng cách nào.
        // Synchronous callback, chạy trước khi Status chuyển sang Completed.
    }

    public void Dispose(MonoTaskNode node)
    {
        // Cleanup resources khi node bị dispose/reset.
    }
}
```

Lưu ý quan trọng:

- Class **phải** có `[Serializable]` để Unity serialize qua `[SerializeReference]`.
- Class **phải** có `[TaskDefinition("Display Name")]` để xuất hiện trong editor dropdown.
- `OnRunning` là nơi logic chính chạy. Luôn check `ct.ThrowIfCancellationRequested()` hoặc truyền `ct` vào các await để hỗ trợ cancel.
- `OnFinishing` được gọi sau OnRunning khi đi qua async flow (ExecuteAsync hoặc ForceComplete khi Running). **Không** được gọi khi `ForceComplete(immediate: true)` hoặc `ForceComplete` từ Pending.
- `OnCompleted` **luôn được gọi** khi node hoàn thành — dù là qua ExecuteAsync, ForceComplete, hay ForceComplete(immediate). Synchronous callback, chạy trước khi Status = Completed.
- `Dispose` được gọi khi node bị reset hoặc dispose — cleanup resources ở đây.
- Nếu exception (non-cancellation) xảy ra trong OnRunning/OnFinishing, node chuyển sang `Failed` và exception được log. Task **không** crash silently.

### 2. Đăng ký qua [TaskDefinition] attribute

Thêm `[TaskDefinition("Display Name")]` lên class implement `ITaskDefinition`. Class sẽ tự động xuất hiện trong editor dropdown — không cần đăng ký thủ công.

```csharp
[Serializable]
[TaskDefinition("Move To Position", Description = "Di chuyển đến vị trí target")]
public class MoveToTask : ITaskDefinition { ... }
```

Attribute properties:

- `DisplayName` (bắt buộc) — tên hiển thị trong editor dropdown.
- `Description` (tùy chọn) — mô tả task, phục vụ cho người dùng editor và AI khi sinh JSON.
- `BindingMode` (tùy chọn, mặc định `ByDisplayName`) — cách binder identify type khi serialize/deserialize JSON:
  - `ByDisplayName` — dùng display name.
  - `ByTypeName` — dùng tên type (không bao gồm namespace).
  - `ByTypeFullName` — dùng full name bao gồm namespace, cần thiết khi có class trùng tên giữa các namespace.

### 3. Dependency Injection (tùy chọn)

DI là **opt-in**. Task definition chỉ nhận DI khi implement `IDependencyInjectionVisitable`:

```csharp
[Serializable]
[TaskDefinition("Move To")]
public class MoveToTask : ITaskDefinition, IDependencyInjectionVisitable
{
    [NonSerialized] public PlayerController player;

    public void Accept(IDependencyInjectionVisitor visitor) => visitor.Visit(this);

    public async UniTask OnRunning(MonoTaskNode node, CancellationToken ct)
    {
        // Dùng player đã được inject
        await player.MoveTo(targetPosition, ct);
    }

    // ... OnFinishing, OnCompleted, Dispose
}
```

Tạo visitor:

```csharp
public class GameVisitor : IDependencyInjectionVisitor
{
    readonly PlayerController _player;
    readonly DialogueSystem _dialogue;

    public GameVisitor(PlayerController player, DialogueSystem dialogue)
    {
        _player = player;
        _dialogue = dialogue;
    }

    public void Visit<T>(T target)
    {
        if (target is MoveToTask move) move.player = _player;
        if (target is DialogueTask dlg) dlg.dialogueSystem = _dialogue;
    }
}

// Gọi trước Execute:
taskTree.Accept(new GameVisitor(player, dialogue));
taskTree.Execute();
```

Task definition **không** implement `IDependencyInjectionVisitable` sẽ được bỏ qua khi Accept — không lỗi, không side effect.

### 4. Xây dựng cây trong Editor

- `TaskTree` là serializable class — nhúng nó như field trong MonoBehaviour hoặc ScriptableObject của bạn.
- Inspector tự động hiện **TaskTree PropertyDrawer** inline khi chọn object chứa TaskTree field. Gồm 3 phần: Import/Export buttons, Hierarchy panel (cây node), và Inspector panel (chi tiết node đang chọn).
- Root node đã được tạo sẵn. Thêm children, gán task definition cho MonoTaskNode.

### 5. Execute

```csharp
var cts = taskTree.Execute();

// Cancel từ bên ngoài khi cần:
cts.Cancel();
```

## Best Practices

**Thiết kế ITaskDefinition:**

- Mỗi task definition nên làm **một việc duy nhất**. "Di chuyển đến vị trí", "Hiển thị dialogue", "Chờ input" — không phải "Di chuyển rồi nói chuyện rồi chờ".
- Luôn xử lý cancellation đúng cách trong `OnRunning`. Nếu task có vòng lặp hoặc await dài, truyền `CancellationToken` vào hoặc check thường xuyên.
- Đặt logic cleanup có thể async trong `OnFinishing`. Logic cleanup synchronous đặt trong `OnCompleted`.
- `OnCompleted` luôn được gọi bất kể completion path nào — đây là nơi đáng tin cậy nhất cho final cleanup. Nhưng giữ nó nhẹ vì synchronous.
- `Dispose` dùng cho cleanup resources khi node bị reset hoặc destroy. Null-safe — framework gọi `taskDefinition?.Dispose(this)`.
- Chỉ implement `IDependencyInjectionVisitable` khi task definition thực sự cần nhận dependencies từ bên ngoài. Không cần thì bỏ qua.

**Thiết kế cây:**

- Dùng `CompositeTaskNode` Sequential cho flow tuyến tính (bước 1 → bước 2 → bước 3).
- Dùng Parallel khi nhiều việc cần chạy đồng thời (ví dụ: spawn enemies + play music + start timer).
- Lồng Composite để tạo cấu trúc phức tạp: một quest Sequential chứa nhiều objective, mỗi objective là một Composite Parallel chứa các subtask.
- `subTaskValue` kiểm soát trọng số progress. Set 0 cho task không đóng góp vào progress bar (ví dụ: task khởi tạo). Tổng subTaskValue không cần bằng 1 — hệ thống tự normalize.
- `targetProgressToComplete < 1` cho phép complete sớm. Ví dụ: 5 optional objectives với subTaskValue bằng nhau, set `targetProgressToComplete = 0.6` → hoàn thành 3/5 là đủ.
- Dùng `Child.enabled = false` để tạm tắt một nhánh mà không cần xóa khỏi cây.

**Runtime:**

- Gọi `Accept(visitor)` **trước** `Execute()` để đảm bảo dependencies đã inject.
- Giữ reference đến `CancellationTokenSource` trả về từ `Execute()` nếu cần cancel từ bên ngoài.
- `InsertChild` cho phép thêm task vào cây đang chạy. Trong Parallel mode, child mới được execute ngay. Trong Sequential mode, chỉ child insert **sau** task đang chạy mới được execute — child insert trước vị trí hiện tại sẽ bị bỏ qua.

**Data sharing giữa các task:**

- Framework không có built-in data sharing. Nếu task A cần truyền kết quả cho task B, dùng một trong các cách: inject shared object qua DI Visitor, hoặc tạo MonoBehaviour/ScriptableObject chứa shared state và reference từ các task definition.

## Tính năng Editor

TaskTree được render inline trong Unity Inspector qua `TaskTreePropertyDrawer` — không cần mở EditorWindow riêng.

- **Hierarchy panel** — Cây task với expand/collapse, drag-drop reorder, inline rename (F2), search filter, multi-select.
- **Inspector panel** — Chỉnh execution mode, children, subTaskValue, task definition fields. Chọn task definition type qua dropdown có search (SearchablePopup).
- **Runtime monitoring** — Status dot (xanh dương = Running, xanh lá = Completed, đỏ = Failed, xám = Pending), progress bar, Force Complete / Reset buttons khi Play Mode.
- **Keyboard shortcuts** — Arrow keys điều hướng, Ctrl+D duplicate, Ctrl+C/V copy-paste, Delete xóa, Alt+Arrow expand/collapse all.
- **Import/Export JSON** — Buttons nằm trên cùng của PropertyDrawer. Export cây ra file JSON, import từ file JSON.
- **Auto-discovery** — Task definition types được phát hiện tự động qua `[TaskDefinition]` attribute. Không cần đăng ký thủ công hay tạo asset.
- **Odin Inspector** — Khi project có cài Odin (`ODIN_INSPECTOR` defined), PropertyDrawer tự động dùng Odin PropertyTree để vẽ task definition fields, hỗ trợ custom attribute drawers (ShowIf, FoldoutGroup...). Khi không có Odin, dùng reflection-based fallback.

## JSON Import / Export

Export cây task ra JSON và import lại qua các buttons Import/Export trên TaskTree PropertyDrawer.

Deserialization sử dụng `TaskTreeSerializationBinder` — một whitelist binder chỉ cho phép deserialize các type có `[TaskDefinition]` attribute (cùng với các structural types: `CompositeTaskNode`, `MonoTaskNode`). Binder duy trì hai dictionary hai chiều (`Type↔string`) để map giữa type và tên serialization. Mọi type không có attribute sẽ bị reject, chặn type injection từ JSON không tin cậy. `BindToName` trả về `assemblyName = null` để JSON chỉ chứa type name, không chứa assembly info.

Mỗi `[TaskDefinition]` attribute có tùy chọn `BindingMode` quyết định cách binder identify type: `ByDisplayName` (dùng display name, mặc định), `ByTypeName` (dùng tên type), hoặc `ByTypeFullName` (dùng full name bao gồm namespace — cần thiết khi có class trùng tên giữa các namespace khác nhau).

JSON format tuân theo cấu trúc cây: mỗi node chứa `$type`, `name`, `executionMode`/`taskDefinition`, và `children`. Có thể dùng AI để sinh JSON từ mô tả tự nhiên, miễn là AI biết danh sách task definition types (từ `[TaskDefinition]` attributes) và tuân theo format.

## Khái niệm chính

| Khái niệm | Mô tả |
|---|---|
| `TaskTree` | Serializable class entry point. Giữ root node, cung cấp `Execute()` và `Accept()` |
| `CompositeTaskNode` | Node cha, chứa children. Chạy Sequential hoặc Parallel |
| `MonoTaskNode` | Node lá. Wrap một `ITaskDefinition` |
| `ITaskDefinition` | Interface user implement. Chứa logic cụ thể của task |
| `[TaskDefinition]` | Attribute đăng ký ITaskDefinition cho auto-discovery trong editor |
| `TaskDefinitionRegistry` | Editor utility quét assembly tìm `[TaskDefinition]`, cache kết quả |
| `IDependencyInjectionVisitable` | Opt-in interface để task definition nhận DI qua visitor |
| `IDependencyInjectionVisitor` | Visitor để inject dependencies vào task definitions trước khi execute |
| `TaskNodeStatus` | Pending, Running, Finishing, Completed, Failed |
| `OnCompleted()` | Điểm hoàn thành duy nhất — mọi completion path bình thường đều đi qua đây |
| `Dispose()` | Cleanup resources khi node bị reset/dispose. Null-safe |
| `subTaskValue` | Trọng số đóng góp vào progress của parent (0 = không đóng góp) |
| `targetProgressToComplete` | Ngưỡng progress để auto-complete composite (mặc định 1.0) |
| `ForceComplete()` | Cancel Running, chờ Finishing, rồi OnCompleted |
| `ForceComplete(immediate)` | Cancel tất cả, gọi OnCompleted ngay |
| `TaskTreeSerializationBinder` | Whitelist binder cho JSON deserialization, chặn type injection |
