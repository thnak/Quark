ĐẶC TẢ KỸ THUẬT: GRAIN PROXY CODEC & DATA ISOLATION PIPELINETài liệu này đặc tả kiến trúc và thiết kế kỹ thuật của hệ thống Codec (Mã hóa/Giải mã) tự chế, được sinh tự động bằng Source Generator dành riêng cho giải pháp Grain Proxy.Mục tiêu cốt lõi của giải pháp là đảm bảo Tính cô lập dữ liệu (Data Isolation) tuyệt đối giữa Caller và Grain Instance (tránh lỗi chia sẻ tham chiếu - Reference Sharing) trong khi vẫn duy trì Hiệu năng cực cao (Zero-Allocation/Fast-Path) khi chạy trên cùng một tiến trình (Local Call).1. Nguyên Lý Thiết Kế Tổng Quan (Architecture)Hệ thống tự động phát hiện ngữ cảnh cuộc gọi thông qua một cờ định tuyến (IsLocal) để quyết định cơ chế xử lý dữ liệu:                          [ Client / Proxy Call ]
                                     |
                    +----------------+----------------+
                    |                                 |
             [ IsLocal == true ]              [ IsLocal == false ]
                    |                                 |
             ( FAST PATH: Local )             ( NETWORK PATH: Remote )
                    |                                 |
           Source Generated Clone()          Serialize sang ReadOnlyMemory<byte>
                    |                                 |
          [ Trực tiếp gọi Grain ]               [ Gửi qua Dispatcher/Socket ]
                    |                                 |
                    |                                 v
                    |                      [ Nhận ReadOnlyMemory<byte> ]
                    |                                 |
                    |                         ( Deferred Deserialize )
                    |                                 |
                    +---------------->  [ Grain Execution ]
Các trạng thái dữ liệu trong pipeline:Local Path (Fast Path): Sử dụng cơ chế Fast Copy (Deep/Shallow Clone) được tạo tự động bởi Source Generator. Loại bỏ hoàn toàn bước mã hóa sang mảng byte, giảm thiểu áp lực lên Garbage Collector (GC) và CPU xuống mức tiệm cận 0.Remote Path (Network Path): Sử dụng ReadOnlyMemory<byte> làm lớp vỏ bọc vận chuyển chung. Quá trình giải mã (Deserialization) được trì hoãn (Deferred) cho tới giây phút cuối cùng khi tiếp cận Grain đích ở Server đầu xa.2. Đặc Tả Chi Tiết Tầng Sinh Mã (Source Generated Codec)Source Generator sẽ quét toàn bộ các kiểu dữ liệu truyền vào phương thức của Grain (gọi là DTO hoặc Arguments Struct) và tự động sinh ra hai thành phần:Fast-Copy Codec (Clone): Sao chép nhanh đối tượng.Buffer Codec (Serialize/Deserialize): Chuyển đổi nhị phân.2.1. Cấu Trúc Giao Diện Hợp Đồng (Codec Contract)Để đảm bảo tính nhất quán, mọi kiểu dữ liệu tham số được sinh mã sẽ đi kèm một giao diện Codec tĩnh (static interface) hoặc mã biên dịch tĩnh dạng helper:public interface IGrainCodec<T>
{
    // Fast Path (Local)
    T Clone(T source);

    // Remote Path (Network)
    void Serialize(ref ArrayBufferWriter<byte> writer, T value);
    T Deserialize(ReadOnlyMemory<byte> source);
}
2.2. Đặc Tả Thuật Toán Sinh Hàm Clone (Fast Copy Generator)Hàm Clone được sinh ra tự động dựa trên các nguyên tắc phân loại kiểu dữ liệu sau:Loại kiểu dữ liệu (Data Type)Hành vi sao chép (Cloning Behavior)Ghi chú hiệu năngValue Types (int, long, struct, Guid,...)Sao chép gán trực tiếp (=)Do .NET tự copy theo dạng value trên Stack, hiệu năng tức thời.Primitive Immutable Types (string)Giữ nguyên tham chiếuKhông cần clone vì bản thân chuỗi là bất biến (Immutable).User Immutable Types (Được đánh dấu [Immutable])Giữ nguyên tham chiếuBỏ qua bước clone để đạt hiệu năng tối đa.Standard Collections (List<T>, Dictionary<K, V>)Khởi tạo mới và duyệt clone phần tử bên trongCần sinh mã duyệt for / foreach thủ công, tránh dùng LINQ để tối ưu AOT.Nested DTO/ClassesĐệ quy gọi hàm Clone của DTO conGọi chéo sang Codec tương ứng của kiểu dữ liệu con.3. Bản Vẽ Hiện Thực Mã Nguồn (Code Generator Output)Dưới đây là ví dụ minh họa cấu trúc mã nguồn được sinh ra tự động bởi Source Generator khi biên dịch Grain sau:public interface IUserGrain : IGrain
{
    ValueTask<UserResponse> UpdateUserAsync(int id, string name, List<string> roles);
}
3.1. Generated Arguments Struct & Codec// --- CODE DO SOURCE GENERATOR TỰ ĐỘNG SINH RA ---

namespace YourSystem.Generated
{
    // 1. Đóng gói tham số truyền vào thành một Struct duy nhất
    public struct UpdateUser_Args
    {
        public int Id;
        public string Name;
        public List<string> Roles;
    }

    // 2. Bộ Codec chuyên biệt cho UpdateUser_Args nhằm triệt tiêu Reflection
    public static class UpdateUser_Args_Codec
    {
        // FAST PATH: Hàm nhân bản cực nhanh cho các cuộc gọi Local
        public static UpdateUser_Args Clone(UpdateUser_Args source)
        {
            var target = new UpdateUser_Args();
            
            // Sao chép kiểu nguyên thủy (Value Type / Immutable String)
            target.Id = source.Id;
            target.Name = source.Name;

            // Sao chép mảng/danh sách thủ công (Deep Copy Collection)
            if (source.Roles != null)
            {
                var newList = new List<string>(source.Roles.Count);
                for (int i = 0; i < source.Roles.Count; i++)
                {
                    newList.Add(source.Roles[i]); // string là bất biến nên không cần clone phần tử
                }
                target.Roles = newList;
            }

            return target;
        }

        // REMOTE PATH: Chuyển đổi nhị phân tốc độ cao (AOT Friendly)
        public static void Serialize(System.Buffers.IBufferWriter<byte> writer, UpdateUser_Args value)
        {
            // Tích hợp trực tiếp với thư viện Serializer hiệu năng cao (như MemoryPack/MessagePack)
            // Code ghi nhị phân tuyến tính, không dùng Reflection
            var state = new SerializerState(writer);
            state.WriteInt32(value.Id);
            state.WriteString(value.Name);
            state.WriteList(value.Roles);
        }

        public static UpdateUser_Args Deserialize(ReadOnlyMemory<byte> source)
        {
            var reader = new ByteReader(source);
            var value = new UpdateUser_Args();
            value.Id = reader.ReadInt32();
            value.Name = reader.ReadString();
            value.Roles = reader.ReadList<string>();
            return value;
        }
    }
}
3.2. Generated Smart Proxy (Phía Client)Tầng Proxy sẽ tự động chuyển hướng dựa trên việc Grain nằm ở Local hay Remote:// --- CODE DO SOURCE GENERATOR TỰ ĐỘNG SINH RA ---

public class UserGrainProxy : IUserGrain
{
    private readonly IGrainInvoker _invoker;
    private readonly Grain _grainTarget;
    private readonly bool _isLocal;
    private readonly uint _grainId;

    public UserGrainProxy(uint grainId, Grain grainTarget, bool isLocal, IGrainInvoker invoker)
    {
        _grainId = grainId;
        _grainTarget = grainTarget;
        _isLocal = isLocal;
        _invoker = invoker;
    }

    public async ValueTask<UserResponse> UpdateUserAsync(int id, string name, List<string> roles)
    {
        const uint methodId = 392818; // Hash từ "UpdateUserAsync"
        
        var originalArgs = new UpdateUser_Args { Id = id, Name = name, Roles = roles };

        if (_isLocal)
        {
            // === FAST PATH: GỌI LOCAL ===
            // Nhân bản dữ liệu cực nhanh thông qua Codec sinh sẵn để cách ly bộ nhớ
            var clonedArgs = UpdateUser_Args_Codec.Clone(originalArgs);
            
            // Chuyển thẳng đối tượng clonedArgs đi mà không cần băm ra byte
            object localResult = await _invoker.InvokeLocalAsync(_grainTarget, methodId, clonedArgs);
            
            // Vì là local call, ta cũng Clone kết quả trả về để đảm bảo an toàn cho Client nhận
            return UserResponse_Codec.Clone((UserResponse)localResult);
        }
        else
        {
            // === REMOTING PATH: GỌI QUA MẠNG ===
            // Serialize thành mảng byte trung gian để gửi đi xa
            using var buffer = new ArrayBufferWriter<byte>();
            UpdateUser_Args_Codec.Serialize(buffer, originalArgs);
            
            ReadOnlyMemory<byte> responseBytes = await _invoker.InvokeRemoteAsync(_grainId, methodId, buffer.WrittenMemory);
            
            // Giải nén kết quả nhận về
            return UserResponse_Codec.Deserialize(responseBytes);
        }
    }
}
4. Đánh Giá Hiệu Năng So Với Giải Pháp Truyền ThốngChỉ số đo lường (Metric)Giải pháp truyền thống (Reflection + Object[])Giải pháp Codec Phân Nhánh Thông MinhĐánh giá cải tiếnLocal Call CPU OverheadRất cao (Do JIT phải tạo mảng object[] và đóng hộp boxing các giá trị nguyên thủy).Cực thấp (Chỉ là các phép gán biến trực tiếp tuần tự).Nhanh hơn x5 - x10 lần.Local Call AllocationsPhát sinh rác liên tục trên Heap từ object[] và boxing.0 Bytes cấp phát thêm đối với struct nông (no nested list) hoặc tối thiểu nhất có thể.Giảm đáng kể áp lực dọn rác GC.Tính tương thích AOTKém (Vì sử dụng kiểu nạp động dễ bị Compiler cắt tỉa - Trimmed).Hoàn hảo (Mã nguồn tường minh rõ ràng, Native AOT biên dịch trực tiếp ra mã máy dễ dàng).Tương thích Native AOT 100%.Tính an toàn luồngDễ bị thay đổi dữ liệu ngoài ý muốn nếu chia sẻ tham chiếu trực tiếp.Cô lập bộ nhớ an toàn tự động bằng cơ chế Deep Copy của hàm Clone().Tuyệt đối an toàn.5. Kết Luận & Hướng Phát Triển Tiếp TheoViệc chia tách Codec thành hai nhánh Fast-Copy (Local) và Binary Serialization (Remote) là chìa khóa vàng giúp hệ thống Grain Proxy tự chế của bạn đạt được đẳng cấp hiệu năng tiệm cận với các ông lớn như Microsoft Orleans hay Akka.NET.Các bước khuyến nghị triển khai tiếp theo:Hoàn thiện Source Generator: Viết bộ sinh mã phân tích thuộc tính (Attribute Analyzer) để tự động phát hiện các class được đánh dấu [Immutable], từ đó sinh mã bỏ qua việc clone (bypass) nhằm vắt kiệt hiệu năng cho các cấu hình Read-Only.Buffer Pooling: Tích hợp System.Buffers.ArrayPool<byte> vào bộ sinh mã Serialize để tái sử dụng tối đa các phân đoạn bộ nhớ đệm khi cuộc gọi buộc phải đi qua mạng (Remote Path).
