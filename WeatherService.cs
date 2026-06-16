using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace OrokinMonitor
{
    public sealed class Weather
    {
        public float? TempC;
        public string Description = "—";
    }

    // Open-Meteo: free, no API key, no sign-up. https://open-meteo.com/
    public sealed class WeatherService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

        // A geocoding match for the city picker.
        public sealed class GeoResult
        {
            public string Name = "";
            public string Country = "";
            public string Admin1 = "";   // state/region, helps disambiguate
            public double Latitude;
            public double Longitude;
            public string Display => string.IsNullOrEmpty(Admin1)
                ? $"{Name}, {Country}"
                : $"{Name}, {Admin1}, {Country}";
        }

        // City name -> candidate coordinates, via Open-Meteo geocoding (free, no key).
        public async Task<System.Collections.Generic.List<GeoResult>> SearchCityAsync(string query)
        {
            var list = new System.Collections.Generic.List<GeoResult>();
            if (string.IsNullOrWhiteSpace(query)) return list;
            try
            {
                string url =
                    $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}" +
                    "&count=8&language=en&format=json";
                using var resp = await _http.GetAsync(url);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("results", out var results))
                {
                    foreach (var r in results.EnumerateArray())
                    {
                        var g = new GeoResult
                        {
                            Name = Str(r, "name"),
                            Country = Str(r, "country"),
                            Admin1 = Str(r, "admin1"),
                            Latitude = r.GetProperty("latitude").GetDouble(),
                            Longitude = r.GetProperty("longitude").GetDouble(),
                        };
                        list.Add(g);
                    }
                }
            }
            catch { /* return what we have */ }
            return list;
        }

        private static string Str(JsonElement e, string prop) =>
            e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? "" : "";

        public async Task<Weather> GetAsync(double lat, double lon)
        {
            var w = new Weather();
            try
            {
                string url =
                    $"https://api.open-meteo.com/v1/forecast?latitude={lat:0.####}" +
                    $"&longitude={lon:0.####}&current=temperature_2m,weather_code";
                using var resp = await _http.GetAsync(url);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var cur = doc.RootElement.GetProperty("current");
                w.TempC = cur.GetProperty("temperature_2m").GetSingle();
                int code = cur.GetProperty("weather_code").GetInt32();
                w.Description = CodeToText(code);
            }
            catch
            {
                w.Description = "unavailable";
            }
            return w;
        }

        // WMO weather interpretation codes -> short text.
        private static string CodeToText(int c) => c switch
        {
            0 => "Clear",
            1 or 2 => "Partly cloudy",
            3 => "Overcast",
            45 or 48 => "Fog",
            51 or 53 or 55 => "Drizzle",
            56 or 57 => "Freezing drizzle",
            61 or 63 or 65 => "Rain",
            66 or 67 => "Freezing rain",
            71 or 73 or 75 => "Snow",
            77 => "Snow grains",
            80 or 81 or 82 => "Showers",
            85 or 86 => "Snow showers",
            95 => "Thunderstorm",
            96 or 99 => "Thunderstorm + hail",
            _ => "—",
        };
    }
}
