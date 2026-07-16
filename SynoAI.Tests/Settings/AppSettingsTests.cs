using SynoAI.Settings;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Xunit;

namespace SynoAI.Tests.Settings
{
    public class AppSettingsTests
    {
        private static List<ValidationResult> Validate(AppSettings settings)
        {
            List<ValidationResult> results = new();
            Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);
            return results;
        }

        [Fact]
        public void Defaults_AreValid()
        {
            Assert.Empty(Validate(new AppSettings()));
        }

        [Fact]
        public void MaxSize_DefaultsToNoMaximumSentinel()
        {
            AppSettings settings = new();
            Assert.Equal(int.MaxValue, settings.MaxSizeX);
            Assert.Equal(int.MaxValue, settings.MaxSizeY);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void MaxSnapshots_BelowOne_FailsValidation(int value)
        {
            AppSettings settings = new() { MaxSnapshots = value };
            Assert.Contains(Validate(settings), r => r.MemberNames.Contains(nameof(AppSettings.MaxSnapshots)));
        }

        [Fact]
        public void FontSize_Zero_FailsValidation()
        {
            AppSettings settings = new() { FontSize = 0 };
            Assert.Contains(Validate(settings), r => r.MemberNames.Contains(nameof(AppSettings.FontSize)));
        }

        [Fact]
        public void Serialization_UsesConfigurationKeyNames_SoTheSettingsFileRoundTrips()
        {
            // The writable settings file is layered back into configuration, so the serialized JSON must
            // use the same keys the configuration binder expects: "User" (not "Username") and
            // "ApiVersionInfo" (not "ApiVersionAuth").
            AppSettings settings = new() { Username = "camuser", ApiVersionAuth = 7 };

            string json = JsonSerializer.Serialize(settings);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            Assert.True(root.TryGetProperty("User", out JsonElement user));
            Assert.Equal("camuser", user.GetString());
            Assert.True(root.TryGetProperty("ApiVersionInfo", out JsonElement apiVersion));
            Assert.Equal(7, apiVersion.GetInt32());

            Assert.False(root.TryGetProperty("Username", out _));
            Assert.False(root.TryGetProperty("ApiVersionAuth", out _));
        }
    }
}
