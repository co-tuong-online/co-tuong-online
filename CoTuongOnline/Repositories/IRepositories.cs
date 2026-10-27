using CoTuongOnline.Models;

namespace CoTuongOnline.Repositories;

// Interface repository: service chỉ phụ thuộc vào các interface này.
// Có 2 bản cài đặt, chọn bằng cấu hình "Database:Provider" trong appsettings:
//   - SqlServer (mặc định): Data/EfRepositories.cs, lưu vào SQL Server qua EF Core.
//   - InMemory: Repositories/InMemory, lưu RAM (mất khi tắt server) — dùng khi máy chưa có SQL Server.

public interface IUserRepository
{
    Task<User?> GetByIdAsync(int id);
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByNicknameAsync(string nickname);
    Task<List<User>> SearchByNicknameAsync(string keyword, int excludeUserId, int limit);
    Task<List<User>> GetByIdsAsync(IEnumerable<int> ids);
    Task<User> AddAsync(User user);
    Task UpdateAsync(User user);
    Task DeleteAsync(User user);
    /// <summary>Xoá tài khoản chưa xác nhận OTP, tạo trước thời điểm cutoff. Trả về số tài khoản đã xoá.</summary>
    Task<int> DeleteUnverifiedCreatedBeforeAsync(DateTime cutoff);
}

public interface IOtpRepository
{
    Task<OtpCode?> GetLatestAsync(string email, string purpose);
    Task<OtpCode> AddAsync(OtpCode otp);
    Task UpdateAsync(OtpCode otp);
}

public interface IGameRepository
{
    Task<GameRecord> AddAsync(GameRecord game);
    Task UpdateAsync(GameRecord game);
    Task<GameRecord?> GetByIdAsync(long id);
    /// <summary>
    /// Ghi 1 nước đi và cập nhật trạng thái ván trong CÙNG một lần lưu
    /// (EF Core: một SaveChanges = một transaction → không bao giờ có nước đi mà ván chưa cập nhật).
    /// </summary>
    Task RecordMoveAsync(GameMoveRecord move, string currentStateJson);
    Task<List<GameRecord>> GetRecentByUserAsync(int userId, int limit);
    /// <summary>
    /// Ván ONLINE còn "PLAYING" trong DB khi server khởi động = ván bị cắt ngang do tắt server
    /// (phòng và đồng hồ chỉ sống trong RAM nên không thể chơi tiếp) → chuyển sang ABORTED.
    /// </summary>
    Task<int> AbortUnfinishedOnlineGamesAsync();
}

public interface ISavedGameRepository
{
    Task<List<SavedGame>> ListByUserAsync(int userId);
    Task<SavedGame?> GetByIdAsync(long id);
    Task<int> CountByUserAsync(int userId);
    Task<SavedGame> AddAsync(SavedGame saved);
    Task UpdateAsync(SavedGame saved);
    Task DeleteAsync(long id);
}
