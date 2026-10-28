using HrmsAi.Access.Data;
using HrmsAi.Access.Models;

namespace HrmsAi.Access.Services;

// Everything the admin UI can do to keys, with validation
public class ApiKeyService(ApiKeyRepository repo, KeyCache cache)
{
    private static readonly string[] Periods = { "Daily", "Monthly", "Total" };

    private record Clean(string Name, string Modules, long? Limit, string Period, DateTime? Expires, string? Notes);

    public async Task<(ApiKeyRecord Record, string Key)> CreateAsync(KeyRequest req, string[] knownModules)
    {
        var c = Validate(req, knownModules);
        var key = KeyGenerator.NewKey();
        var rec = new ApiKeyRecord
        {
            Id = Guid.NewGuid(),
            ClientName = c.Name,
            KeyPrefix = KeyGenerator.DisplayPrefix(key),
            KeyHash = KeyGenerator.Hash(key),
            IsActive = true,
            AllowedModules = c.Modules,
            TokenLimit = c.Limit,
            LimitPeriod = c.Period,
            ExpiresAt = c.Expires,
            Notes = c.Notes,
            CreatedAt = DateTime.UtcNow
        };
        await repo.InsertAsync(rec);
        return (rec, key);
    }

    public async Task<bool> UpdateAsync(Guid id, KeyRequest req, string[] knownModules)
    {
        var c = Validate(req, knownModules);
        var ok = await repo.UpdateSettingsAsync(id, c.Name, c.Modules, c.Limit, c.Period, c.Expires, c.Notes);
        cache.Clear();
        return ok;
    }

    public async Task<bool> SetActiveAsync(Guid id, bool active)
    {
        var ok = await repo.SetActiveAsync(id, active);
        cache.Clear();
        return ok;
    }

    // Gives the client a new secret; the old one stops working immediately
    public async Task<(ApiKeyRecord Record, string Key)?> RotateAsync(Guid id)
    {
        var rec = await repo.GetAsync(id);
        if (rec is null) return null;
        var key = KeyGenerator.NewKey();
        rec.KeyHash = KeyGenerator.Hash(key);
        rec.KeyPrefix = KeyGenerator.DisplayPrefix(key);
        await repo.ReplaceKeyAsync(id, rec.KeyHash, rec.KeyPrefix);
        cache.Clear();
        return (rec, key);
    }

    public static KeyView ToView(ApiKeyRecord r, long used)
    {
        var expired = r.ExpiresAt is not null && r.ExpiresAt.Value.Date.AddDays(1) <= DateTime.UtcNow;
        var limitHit = r.TokenLimit is not null && used >= r.TokenLimit;
        var status = !r.IsActive ? "Deactivated" : expired ? "Expired" : limitHit ? "Limit reached" : "Active";
        var modules = r.AllowedModules == "*"
            ? new[] { "*" }
            : r.AllowedModules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new KeyView(r.Id, r.ClientName, r.KeyPrefix, r.IsActive, modules, r.TokenLimit, r.LimitPeriod,
                           used, r.ExpiresAt, r.Notes, r.CreatedAt, r.LastUsedAt, status);
    }

    private static Clean Validate(KeyRequest r, string[] known)
    {
        var name = r.ClientName?.Trim() ?? "";
        if (name.Length == 0 || name.Length > 200)
            throw new ArgumentException("Client name is required (maximum 200 characters).");

        var wanted = string.IsNullOrWhiteSpace(r.LimitPeriod) ? "Monthly" : r.LimitPeriod.Trim();
        var period = Periods.FirstOrDefault(p => p.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                     ?? throw new ArgumentException("Period must be Daily, Monthly or Total.");

        if (r.TokenLimit is < 0) throw new ArgumentException("The token limit cannot be negative.");

        var asked = (r.AllowedModules ?? Array.Empty<string>())
            .Select(m => m.Trim()).Where(m => m.Length > 0).ToArray();
        if (asked.Length == 0) throw new ArgumentException("Choose at least one module.");

        string modules;
        if (asked.Contains("*")) modules = "*";
        else
        {
            var bad = asked.FirstOrDefault(m => !known.Contains(m, StringComparer.OrdinalIgnoreCase));
            if (bad is not null)
                throw new ArgumentException($"Unknown module '{bad}'. Available in this build: {string.Join(", ", known)}.");
            modules = string.Join(",", asked
                .Select(m => known.First(k => k.Equals(m, StringComparison.OrdinalIgnoreCase)))
                .Distinct());
        }

        var notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
        if (notes is not null && notes.Length > 500) throw new ArgumentException("Notes are limited to 500 characters.");

        // The expiry date is valid through the end of that day (UTC)
        return new Clean(name, modules, r.TokenLimit, period, r.ExpiresAt?.Date, notes);
    }
}
