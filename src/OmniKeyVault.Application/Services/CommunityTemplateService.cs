using System.Text.Json;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// v2.0: Community template contribution mechanism.
/// Allows users to create, export, and import custom template definitions
/// that can be shared with the community.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class CommunityTemplateService
{
    private readonly TemplateService _templates;

    public CommunityTemplateService(TemplateService templates) => _templates = templates;

    /// <summary>Creates a new custom template from field definitions.</summary>
    public TemplateDefinition CreateTemplate(string id, string name, string category, string region,
        string? platformId, List<(string Key, string Label, string Kind, bool Sensitive, bool Required)> fields)
    {
        var def = new TemplateDefinition
        {
            Id = id,
            Name = name,
            Category = category,
            Region = region,
            PlatformId = platformId ?? id,
            IntroducedIn = "2.0.0",
            MvpIncluded = false,
            Fields = fields.Select(f => new TemplateFieldDefinition
            {
                Key = f.Key,
                Label = f.Label,
                Kind = f.Kind,
                Sensitive = f.Sensitive,
                Required = f.Required
            }).ToList()
        };

        _templates.LoadFromJson(JsonSerializer.Serialize(def, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true
        }));

        return def;
    }

    /// <summary>Exports a template definition as a JSON string for sharing.</summary>
    public string ExportTemplate(string templateId)
    {
        var def = _templates.Get(templateId);
        return JsonSerializer.Serialize(def, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }

    /// <summary>Imports a template from a JSON string (from community sharing).</summary>
    public TemplateDefinition ImportTemplate(string json)
    {
        return _templates.LoadFromJson(json);
    }

    /// <summary>Saves a template to the user's templates directory for persistence.</summary>
    public void SaveToUserTemplates(TemplateDefinition def)
    {
        var userDir = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "OmniKeyVault", "templates");

        if (!System.IO.Directory.Exists(userDir))
            System.IO.Directory.CreateDirectory(userDir);

        var path = System.IO.Path.Combine(userDir, $"{def.Id}.json");
        var json = JsonSerializer.Serialize(def, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        System.IO.File.WriteAllText(path, json);
    }

    /// <summary>Lists all user-created templates (from %APPDATA%/OmniKeyVault/templates/).</summary>
    public IEnumerable<TemplateDefinition> ListUserTemplates()
    {
        var userDir = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "OmniKeyVault", "templates");

        if (!System.IO.Directory.Exists(userDir)) return Enumerable.Empty<TemplateDefinition>();

        var result = new List<TemplateDefinition>();
        foreach (var file in System.IO.Directory.EnumerateFiles(userDir, "*.json"))
        {
            try
            {
                var json = System.IO.File.ReadAllText(file);
                var def = JsonSerializer.Deserialize<TemplateDefinition>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    PropertyNameCaseInsensitive = true
                });
                if (def != null && !string.IsNullOrEmpty(def.Id))
                    result.Add(def);
            }
            catch { /* skip invalid */ }
        }
        return result;
    }

    /// <summary>Deletes a user-created template.</summary>
    public bool DeleteUserTemplate(string templateId)
    {
        var userDir = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "OmniKeyVault", "templates");

        var path = System.IO.Path.Combine(userDir, $"{templateId}.json");
        if (System.IO.File.Exists(path))
        {
            System.IO.File.Delete(path);
            return true;
        }
        return false;
    }
}
