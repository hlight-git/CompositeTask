## Kiến trúc `CompositeTask`

### 1. Mục tiêu

- **Mô hình hoá flow gameplay / logic** dưới dạng **cây task (TaskTree)** có thể chạy được ở runtime.
- **Tách biệt rõ** giữa:
  - **Logic task** (implement `ITaskDefinition`)
  - **Runtime node** quản lý trạng thái, tiến độ, hủy, event (`ATaskNode` và các lớp con)
  - **Công cụ editor** để design / chỉnh sửa TaskTree trực quan.
- **Mở rộng tốt** cho AI / tool tự động sinh TaskTree từ kịch bản và registry task.

---

### 2. Tầng Runtime (`Runtime/`)

> Được mô tả chi tiết hơn trong `CompositeTask_Runtime.md`. Ở đây chỉ tóm tắt dưới góc nhìn kiến trúc.

- **Các thành phần chính**
  - `TaskNodeStatus`: enum trạng thái (`Pending`, `Running`, `Completed`).
  - `ExecutionMode`: enum chế độ chạy cho `CompositeTaskNode` (`Sequential`, `Parallel`).
  - `ITaskDefinition`:
    - Giao diện chứa **logic của một task lá**:
      - `OnBegin(MonoTaskNode, CancellationToken)`
      - `OnEnd(MonoTaskNode, CancellationToken)`
      - `OnCompleted(MonoTaskNode)`
  - `ATaskNode` (trong `ARuntimeTaskNode.cs`):
    - Base class trừu tượng cho toàn bộ node trong cây.
    - Quản lý:
      - `Status`, `Progress` \[0,1\]
      - Hai pha **Begin/End** với 2 `CancellationTokenSource` riêng.
      - Event: `ProgressChanged`, `Completed`.
      - Vòng đời `ExecuteAsync`, `Reset`, `ForceComplete`, `ForceCompleteImmediate`, `Dispose`.
  - `MonoTaskNode`:
    - Node **lá**, giữ một `ITaskDefinition` bằng `SerializeReference`.
    - Uỷ quyền `OnTaskBegin` / `OnTaskEnd` sang `taskDefinition`.
  - `CompositeTaskNode`:
    - Node **hợp thành**, chứa nhiều `Child`:
      - `subTaskValue`: trọng số progress.
      - `taskNode`: một `ATaskNode` (mono hoặc composite).
    - Chạy child **tuần tự** (`Sequential`) hoặc **song song** (`Parallel`).
    - Gộp progress các child theo trọng số.
    - Cho phép **thêm child động** khi đang `Running`.
  - `TaskTree`:
    - `MonoBehaviour` entry point trong scene.
    - Cấu hình `rootNode` (thường là `CompositeTaskNode` gốc).
    - API `Execute()` trả về `CancellationTokenSource` để có thể hủy toàn bộ cây.

**Ý tưởng kiến trúc chính**

- Runtime chỉ quan tâm đến:
  - **Cấu trúc cây** (node, children, execution mode)
  - **Vòng đời thực thi & trạng thái**
- Logic game cụ thể nằm trong các lớp implement `ITaskDefinition` (ở assembly game).
- Runtime không phụ thuộc vào editor hay AI – chỉ cần một cấu trúc node đúng là chạy được.

---

### 3. Tầng Editor (`Editor/`)

Mục tiêu của tầng Editor là:

- Cung cấp **UI/UX** để:
  - Chọn loại task (`ITaskDefinition`) từ một **database cấu hình trước**.
  - Dựng / chỉnh sửa cây `TaskTree` trực quan.
  - Import / export cấu trúc cây qua JSON/text để dễ integrate với AI / tool khác.

Các thành phần chính:

- `TaskDefinitionDatabase`:
  - `ScriptableObject` chứa danh sách các task cho phép dùng trong editor.
  - Mỗi `Entry`:
    - `displayName`: tên hiển thị (fallback sang tên type nếu rỗng).
    - `description`: mô tả ngắn cho designer / user.
    - `script`: `MonoScript` trỏ vào class implement `ITaskDefinition`.
  - Được tạo từ menu:
    - `Create → Task Tree → Task Definition Database`
  - Đóng vai trò **registry** cho:
    - Editor (TaskTreeEditorWindow)
    - AI (khi cần ánh xạ từ kịch bản sang task).

- `TaskTreeEditorWindow`:
  - Custom editor window để:
    - Xem và chỉnh sửa `TaskTree`/`CompositeTaskNode`/`MonoTaskNode`.
    - Tạo / xoá / reorder child.
    - Chọn `ExecutionMode` (Sequential/Parallel).
    - Gán `ITaskDefinition` từ `TaskDefinitionDatabase`.
  - Có thể hỗ trợ import / export JSON (kết hợp với tài liệu hướng dẫn AI).

- Các inspector / editor phụ trợ khác:
  - `TaskDefinitionDatabaseEditorWindow`, `TaskTreeInspector`, v.v.
  - Tập trung vào việc:
    - Quản lý database task.
    - Cải thiện trải nghiệm chỉnh sửa TaskTree trong Unity.

**Vai trò của Editor layer**

- Đóng vai trò **cầu nối** giữa:
  - Designer / người dùng (tương tác trong Unity).
  - Dữ liệu runtime (`TaskTree`, `ATaskNode`).
- Không bắt buộc cho runtime chạy, nhưng:
  - Giúp việc tạo / debug TaskTree trở nên trực quan.
  - Tạo điểm hook cho workflow AI/JSON.

---

### 4. Tầng AI / JSON (`TaskTreeFromScenarioPrompt.md`)

File `TaskTreeFromScenarioPrompt.md` định nghĩa **hướng dẫn cho AI** để:

- Nhận:
  - **Kịch bản (scenario)**: mô tả flow / hành vi mong muốn trong game.
  - **Task registry**: danh sách `ITaskDefinition` khả dụng (lấy từ `TaskDefinitionDatabase`).
- Sinh ra:
  - Một **TaskTree JSON** mô tả đầy đủ:
    - Root `CompositeTaskNode`.
    - Cấu trúc children (Sequential/Parallel).
    - Các `MonoTaskNode` gắn đúng `taskDefinition`.
  - Kèm theo phần **"Gợi ý bổ sung task"** cho các hành động chưa map được.

**Đặc điểm JSON**

- Dùng `Newtonsoft.Json` với `TypeNameHandling.Auto`.
- Mỗi node đều có `$type`:
  - `CompositeTaskNode`, `MonoTaskNode` từ assembly `Hlight.Structures.CompositeTask.Runtime`.
  - `taskDefinition` dùng type của game (ví dụ: `MyGame.ShowLogoTask, Assembly-CSharp`).
- Root **luôn là** một `CompositeTaskNode`.
- `children` là danh sách `Child` với:
  - `enabled`
  - `subTaskValue`
  - `taskNode` (Mono/Composite).

**Workflow AI tổng quát**

1. Designer / user viết **kịch bản** tự nhiên.
2. AI:
   - Phân tích kịch bản → chia thành các bước.
   - Map từng bước sang `ITaskDefinition` trong registry.
   - Quyết định **Sequential** vs **Parallel**.
   - Sinh JSON theo schema định nghĩa.
3. Unity:
   - Người dùng paste JSON vào `TextAsset`.
   - Dùng chức năng **Import from TextAsset** trong `TaskTree`/editor để tạo cây runtime.
4. Sau đó, designer có thể tinh chỉnh thêm trong `TaskTreeEditorWindow`.

---

### 5. Luồng dữ liệu tổng quan

1. **ITaskDefinition (Game Code)**:
   - Nhà phát triển game tạo các class implement `ITaskDefinition`.
2. **Đăng ký vào `TaskDefinitionDatabase` (Editor)**:
   - Thêm entry tương ứng, viết mô tả, gán `MonoScript`.
3. **Thiết kế TaskTree**:
   - Thực hiện một trong hai (hoặc kết hợp):
     - Dùng `TaskTreeEditorWindow` để kéo thả, cấu hình trực tiếp.
     - Dùng AI + JSON (theo prompt trong `TaskTreeFromScenarioPrompt.md`) để sinh nhanh skeleton.
4. **Lưu trữ / Import**:
   - Cấu trúc cây có thể được serialize thành JSON (TextAsset) và ngược lại.
5. **Runtime**:
   - `TaskTree` trong scene tham chiếu `rootNode`.
   - Code game gọi `TaskTree.Execute()` để chạy flow.
   - Dùng `CancellationTokenSource`, event progress/completed để điều khiển / quan sát.

---

### 6. Nguyên tắc thiết kế chính

- **Separation of Concerns**:
  - Runtime không biết gì về editor hay AI – nó chỉ xử lý `ATaskNode` + `ITaskDefinition`.
  - Editor/AI không phụ thuộc vào chi tiết implement của từng `ITaskDefinition`, chỉ cần tên type + mô tả.
- **Mở rộng dễ dàng**:
  - Thêm task mới chỉ cần:
    - Tạo class `ITaskDefinition` mới.
    - Đăng ký trong `TaskDefinitionDatabase`.
  - Tầng AI/JSON sẽ tự động có thêm lựa chọn mới khi sinh TaskTree.
- **An toàn & kiểm soát vòng đời**:
  - Sử dụng `CancellationToken` rõ ràng cho từng node.
  - Cho phép reset / force-complete node nếu cần.
- **Thân thiện với designer / AI**:
  - Có schema JSON rõ ràng.
  - Có prompt hướng dẫn AI chi tiết (`TaskTreeFromScenarioPrompt.md`).
  - Có editor window & inspector chuyên dụng để tinh chỉnh sau khi sinh tự động.

---

### 7. TL;DR cho AI Agent

- **Khi sinh TaskTree từ kịch bản**:
  - Chỉ dùng các `ITaskDefinition` có trong `TaskDefinitionDatabase`.
  - Root luôn là **một** `CompositeTaskNode` của `Hlight.Structures.CompositeTask.Runtime`.
  - Dùng `executionMode` để quyết định `Sequential` vs `Parallel`.
- **Về JSON**:
  - Mọi node đều có `$type` đúng (`CompositeTaskNode` / `MonoTaskNode` + assembly).
  - Mỗi phần tử children gồm: `enabled`, `subTaskValue`, `taskNode`.
  - Ưu tiên `subTaskValue = 1` nếu không có lý do đặc biệt.
- **Khi gặp hành động chưa map được**:
  - Không tự định nghĩa task mới trong JSON.
  - Thay vào đó, liệt kê trong phần **"Gợi ý bổ sung task"** với tên gợi ý + mô tả + lý do.

