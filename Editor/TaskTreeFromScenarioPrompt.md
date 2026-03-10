# Hướng dẫn cho AI: Dựng TaskTree từ kịch bản

Bạn nhận **hai đầu vào**:
1. **Kịch bản** (scenario): mô tả thô các sự kiện / hành động cần chạy trong game (do người dùng viết).
2. **Task registry**: danh sách các task (ITaskDefinition) hiện có trong project, kèm tên type, mô tả và (nếu có) cách xây dựng data.

Nhiệm vụ của bạn:
- Phân tích kịch bản và **chia thành các bước** hợp lý.
- **Ánh xạ mỗi bước** vào đúng **một task** trong registry (mỗi bước = một MonoTaskNode với taskDefinition tương ứng).
- Quyết định **thứ tự và cấu trúc**: bước nào chạy **tuần tự** (Sequential), bước nào chạy **song song** (Parallel).
- Sinh ra **một cây TaskTree** dưới dạng JSON, đúng schema bên dưới, để người dùng import vào Unity.
- Nếu có hành động trong kịch bản **không khớp** với task nào trong registry → liệt kê rõ trong phần **"Gợi ý bổ sung task"** (tên task đề xuất + mô tả ngắn + lý do).

---

## Cấu trúc TaskTree (tóm tắt)

- **TaskTree** có một **Root** duy nhất, kiểu `CompositeTaskNode` (không bao giờ là MonoTaskNode).
- **CompositeTaskNode**: chứa danh sách **children**; mỗi child là một `ATaskNode` (có thể là MonoTaskNode hoặc CompositeTaskNode con). Có **ExecutionMode**:
  - **Sequential**: chạy lần lượt theo thứ tự children.
  - **Parallel**: chạy tất cả children cùng lúc, composite hoàn thành khi mọi child xong.
- **MonoTaskNode**: node lá, chạy **một** logic duy nhất thông qua **taskDefinition** (implementation của ITaskDefinition). Chỉ dùng các **type đã có trong task registry**.
- **Child**: mỗi phần tử trong `children` có:
  - `enabled`: true/false (có bật node này hay không).
  - `subTaskValue`: số float ≥ 0 (trọng số đóng góp vào progress; thường dùng 1).
  - `taskNode`: một ATaskNode (Mono hoặc Composite).

Quy ước đặt tên node:
- Dùng `name` có ý nghĩa, ngắn gọn, dễ đọc (ví dụ: `Intro_ShowLogo`, `SpawnEnemies`, `WaitForPlayerInput`). Tránh để trống.

---

## Schema JSON (Unity import)

Unity dùng **Newtonsoft.Json** với **TypeNameHandling.Auto**, nên mọi node cần có **$type** để phân biệt kiểu. Bạn **bắt buộc** dùng đúng tên type và (khi cần) assembly như dưới đây.

### Namespace và assembly

- Namespace runtime: `Hlight.Structures.CompositeTask.Runtime`
- Assembly: `Hlight.Structures.CompositeTask.Runtime`

### Các kiểu cần dùng trong JSON

| Kiểu | Ghi chú |
|------|--------|
| `CompositeTaskNode` | Root hoặc node composite con. |
| `MonoTaskNode` | Node lá; bắt buộc có `taskDefinition` với **đúng type** nằm trong task registry. |
| `ExecutionMode` | Enum: `0` = Sequential, `1` = Parallel (hoặc tên: `Sequential` / `Parallel` tùy serializer). |
| `CompositeTaskNode+Child` | Lớp lồng trong CompositeTaskNode (trong JSON có thể là `Child` trong object `children`). |

### Định dạng $type (Newtonsoft)

- Ví dụ với assembly-qualified name:
  - CompositeTaskNode: `Hlight.Structures.CompositeTask.Runtime.CompositeTaskNode, Hlight.Structures.CompositeTask.Runtime`
  - MonoTaskNode: `Hlight.Structures.CompositeTask.Runtime.MonoTaskNode, Hlight.Structures.CompositeTask.Runtime`
- **taskDefinition** trong MonoTaskNode: dùng **chính xác** type name (và assembly nếu có) được cung cấp trong **task registry** (ví dụ: `MyGame.ShowLogoTask, MyGame`).

Nếu task registry chỉ cho **tên class** (không có assembly), bạn có thể thử:
- `TênClass, TênAssembly` (assembly thường trùng với tên project hoặc assembly chứa game code).

---

## Ví dụ JSON (minimal)

Root là một CompositeTaskNode Sequential với hai child: một MonoTaskNode (ShowLogo), một CompositeTaskNode Parallel (hai MonoTaskNode con).

```json
{
  "$type": "Hlight.Structures.CompositeTask.Runtime.CompositeTaskNode, Hlight.Structures.CompositeTask.Runtime",
  "name": "Root",
  "executionMode": 0,
  "children": [
    {
      "enabled": true,
      "subTaskValue": 1,
      "taskNode": {
        "$type": "Hlight.Structures.CompositeTask.Runtime.MonoTaskNode, Hlight.Structures.CompositeTask.Runtime",
        "name": "Intro_ShowLogo",
        "taskDefinition": {
          "$type": "MyGame.ShowLogoTask, Assembly-CSharp"
        }
      }
    },
    {
      "enabled": true,
      "subTaskValue": 1,
      "taskNode": {
        "$type": "Hlight.Structures.CompositeTask.Runtime.CompositeTaskNode, Hlight.Structures.CompositeTask.Runtime",
        "name": "ParallelGroup",
        "executionMode": 1,
        "children": [
          {
            "enabled": true,
            "subTaskValue": 1,
            "taskNode": {
              "$type": "Hlight.Structures.CompositeTask.Runtime.MonoTaskNode, Hlight.Structures.CompositeTask.Runtime",
              "name": "LoadScene",
              "taskDefinition": { "$type": "MyGame.LoadSceneTask, Assembly-CSharp" }
            }
          },
          {
            "enabled": true,
            "subTaskValue": 1,
            "taskNode": {
              "$type": "Hlight.Structures.CompositeTask.Runtime.MonoTaskNode, Hlight.Structures.CompositeTask.Runtime",
              "name": "PreloadAudio",
              "taskDefinition": { "$type": "MyGame.PreloadAudioTask, Assembly-CSharp" }
            }
          }
        ]
      }
    }
  ]
}
```

Lưu ý:
- `executionMode`: `0` = Sequential, `1` = Parallel (theo thứ tự enum trong Unity).
- Tên trong `taskDefinition.$type` **phải** trùng với một entry trong task registry (đúng tên class + assembly).

---

## Quy tắc khi sinh JSON

1. **Root**: luôn là **một** object `CompositeTaskNode` (không phải mảng, không phải MonoTaskNode).
2. **Chỉ dùng task có trong registry**: mỗi MonoTaskNode.taskDefinition phải có `$type` khớp với một task trong danh sách đã cho. Nếu không có task phù hợp cho một bước → không tạo MonoTaskNode tùy ý; thay vào đó ghi rõ trong phần **"Gợi ý bổ sung task"**.
3. **Sequential vs Parallel**:
   - Dùng **Sequential** khi các bước phải xảy ra **theo thứ tự** (A xong rồi B).
   - Dùng **Parallel** khi các bước có thể chạy **cùng lúc** (A và B độc lập).
4. **Child.enabled**: mặc định `true`. Chỉ đặt `false` nếu có lý do rõ (ví dụ: bước tạm tắt để debug).
5. **Child.subTaskValue**: mặc định `1` nếu không có yêu cầu đặc biệt về trọng số progress.
6. **name**: mỗi node nên có `name` ngắn gọn, dễ nhận diện trong editor.

---

## Định dạng output bạn cần trả về

Trả về **đúng hai phần** sau (để người dùng dễ copy JSON vào Unity):

### 1. TaskTree JSON

- Một khối JSON **một object duy nhất** (root CompositeTaskNode), như ví dụ trên.
- Có thể đặt trong markdown code block `json` để dễ copy.

### 2. Gợi ý bổ sung task (nếu có)

- Nếu kịch bản có hành động không map được vào task nào trong registry, liệt kê:
  - **Tên task đề xuất** (ví dụ: `WaitForPopupCloseTask`)
  - **Mô tả ngắn** (task này làm gì, input/output nếu cần)
  - **Lý do** (bước nào trong kịch bản cần task này)

Nếu mọi bước đều map được vào registry thì phần này có thể ghi: "Không cần bổ sung task."

---

## Checklist trước khi gửi JSON

- [ ] Root là **một** CompositeTaskNode.
- [ ] Mọi MonoTaskNode đều có `taskDefinition` với `$type` nằm trong task registry.
- [ ] Mọi node có `name` không trống (trừ khi registry/spec cho phép).
- [ ] executionMode: 0 = Sequential, 1 = Parallel.
- [ ] children là mảng; mỗi phần tử có `enabled`, `subTaskValue`, `taskNode` với `$type` đúng.

Khi người dùng có JSON đúng format, họ sẽ paste vào file .json (TextAsset) trong Unity và dùng nút **Import from TextAsset** trên component TaskTree để nạp cây vào scene.
