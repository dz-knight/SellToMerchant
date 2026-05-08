using System.IO;
using System.Text.Json;

namespace SellToMerchant
{
    public sealed class UpdateState
    {
        public string InstalledVersion { get; set; } = "";
        public string InstalledChannel { get; set; } = "";

        public static UpdateState Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return new UpdateState();

                return JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(path)) ?? new UpdateState();
            }
            catch
            {
                return new UpdateState();
            }
        }

        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
