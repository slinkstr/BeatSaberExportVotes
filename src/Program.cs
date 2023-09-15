using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal class Program
{
    private static HttpClient _httpClient = new();

    private static async Task Main(string[] args)
    {
        try
        {
            Console.WriteLine("Enter Beat Saber path (contains 'Beat Saber.exe')");
            string dirInput = Console.ReadLine() ?? throw new Exception("Invalid path");
            if (!Directory.Exists(dirInput))
            {
                throw new Exception($"Invalid path \"{dirInput}\"");
            }

            string voteFile = Path.Combine(dirInput, "UserData/votedSongs.json");
            if (!File.Exists(voteFile))
            {
                throw new Exception("Unable to locate votedSongs.json");
            }

            List<string> hashes = new();

            string content = File.ReadAllText(voteFile);
            JObject jobj = JObject.Parse(content);

            bool upvote = true;
            Console.WriteLine("[U]pvote (default) or [d]ownvotes?");
            string? upDownInput = Console.ReadLine();
            if (upDownInput != null)
            {
                if (upDownInput.Equals("u", StringComparison.OrdinalIgnoreCase))
                {
                    // do nothing
                }
                else if (upDownInput.Equals("d", StringComparison.OrdinalIgnoreCase))
                {
                    upvote = false;
                }
                else
                {
                    Console.WriteLine("Unrecognized option, defaulting to [u]pvote.");
                }
            }

            string voteString = upvote ? "Upvote" : "Downvote";

            foreach (var (key, val) in jobj)
            {
                string hash = key;
                string vote = val?.SelectToken("voteType")?.Value<string>() ?? throw new Exception("Error getting vote type for map " + hash);

                if (vote.Equals(voteString, StringComparison.OrdinalIgnoreCase))
                {
                    hashes.Add(hash);
                }
            }

            if (hashes.Count < 1)
            {
                throw new Exception("No maps found.");
            }

            List<BeatSaberMap> maps = await BeatSaverMapInfo(hashes);

            BeatSaberPlaylist playlist = new()
            {
                PlaylistAuthor = "BeatSaberExportVotes",
                PlaylistDescription = $"Exported at {DateTime.UtcNow} UTC.",
                PlaylistTitle = $"{voteString}s ({DateTime.UtcNow.ToString("yyyy-MM-dd")})",
                Songs = maps,
            };

            string jsonSerialized = JsonConvert.SerializeObject(playlist, Formatting.None);
            File.WriteAllText($"{voteString}s.bplist", jsonSerialized);
        }
        catch (Exception exc)
        {
            Console.WriteLine(exc.ToString());
            Console.WriteLine();
            ExitPrompt();
        }
    }

    private static void ExitPrompt()
    {
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey();
        Environment.Exit(0);
    }

    private static async Task<List<BeatSaberMap>> BeatSaverMapInfo(List<string> hashes)
    {
        int max = 50;
        List<List<string>> buckets = new();
        List<BeatSaberMap> maps = new();

        for (int i = 0; i < hashes.Count; i += max)
        {
            buckets.Add(hashes.Take(max).ToList());
        }

        string host = "https://api.beatsaver.com";
        string path = "/maps/hash/";
        foreach (var bucket in buckets)
        {
            var response = await _httpClient.GetAsync(host + path + string.Join(",", bucket));
            response.EnsureSuccessStatusCode();
            string content = await response.Content.ReadAsStringAsync();
            JObject jobj = JObject.Parse(content);

            if (bucket.Count == 1)
            {
                string beatSaverId  = jobj.SelectToken("id")?.Value<string>() ?? throw new Exception("Error parsing id from JObject " + jobj.ToString());
                string hash         = bucket[0];
                string name         = jobj.SelectToken("name")?.Value<string>() ?? throw new Exception("Error parsing name from JObject " + jobj.ToString());

                maps.Add(new()
                {
                    BeatSaverId = beatSaverId,
                    Hash = hash,
                    Name = name,
                });
            }
            else
            {
                foreach (var (key, val) in jobj)
                {
                    string hash        = key;
                    string beatSaverId = val?.SelectToken("id")?.Value<string>() ?? throw new Exception("Error parsing id from JObject " + val?.ToString());
                    string name        = val?.SelectToken("name")?.Value<string>() ?? throw new Exception("Error parsing name from JObject " + val?.ToString());

                    maps.Add(new()
                    {
                        BeatSaverId = beatSaverId,
                        Hash = hash,
                        Name = name,
                    });
                }
            }
        }

        return maps;
    }

    private class BeatSaberMap
    {
        [JsonProperty("key")]
        public string BeatSaverId { get; set; } = "";

        [JsonProperty("hash")]
        public string Hash { get; set; } = "";

        [JsonProperty("songName")]
        public string Name { get; set; } = "";
    }

    private class BeatSaberPlaylist
    {
        [JsonProperty("playlistTitle")]
        public string PlaylistTitle { get; set; } = "";

        [JsonProperty("playlistAuthor")]
        public string PlaylistAuthor { get; set; } = "";

        [JsonProperty("playlistDescription")]
        public string PlaylistDescription { get; set; } = "";

        [JsonProperty("image")]
        public string? Image { get; set; } = null;

        [JsonProperty("songs")]
        public List<BeatSaberMap> Songs { get; set; } = new();
    }
}