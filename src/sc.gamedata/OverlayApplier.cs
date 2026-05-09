using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace sc.gamedata
{
	// Copies CryXML-physics fields (size_class, mass, agility, thrust_capacity,
	// scm_speed, etc.) from a previously-built LIVE game_data.json onto the
	// fresh PTU vehicle records by id. The DataForge tree alone doesn't
	// contain these — they're in per-ship `.xml` files inside the .p4k
	// (CryEngine entity definitions, not DataForge records). Until that
	// extraction is wired up, an overlay from a recent LIVE build is the
	// pragmatic source. Hull dimensions don't change between LIVE → PTU for
	// the same id, so the values stay accurate for existing ships.
	internal static class OverlayApplier
	{
		// Fields that ship physics knows but our DataForge pass leaves null.
		// Anything already populated on the PTU record (length/width/height/
		// crew_size/career/role/armor profile) is left alone.
		private static readonly String[] FieldsToCopy = new[]
		{
			"size_class",
			"scm_speed",
			"boost_speed",
			"nav_speed",
			"mass",
			"mass_loadout",
			"mass_total",
			"hull_hp",
			"agility",
			"acceleration",
			"thrust_capacity",
			"cross_section",
		};

		public static Int32 ApplyFromLive(String overlayPath, List<VehicleRecord> vehicles)
		{
			if (!File.Exists(overlayPath))
			{
				Console.WriteLine($"WARN: overlay not found at {overlayPath} — skipping.");
				return 0;
			}
			using var doc = JsonDocument.Parse(File.ReadAllText(overlayPath));
			if (!doc.RootElement.TryGetProperty("vehicles", out var liveVehicles)
				|| liveVehicles.ValueKind != JsonValueKind.Array) return 0;

			var byId = new Dictionary<String, JsonElement>(StringComparer.Ordinal);
			foreach (var v in liveVehicles.EnumerateArray())
			{
				if (v.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
					byId[idEl.GetString()!] = v;
			}

			var updated = 0;
			foreach (var v in vehicles)
			{
				if (!byId.TryGetValue(v.id, out var live)) continue;
				var touched = false;
				foreach (var field in FieldsToCopy)
				{
					if (!live.TryGetProperty(field, out var val) || val.ValueKind == JsonValueKind.Null) continue;
					if (CopyField(v, field, val)) touched = true;
				}
				if (touched) updated++;
			}
			return updated;
		}

		// Reflection-style copy. Only the explicit field set above is touched;
		// any unknown field is silently skipped so an old overlay file with
		// extras doesn't poison PTU output.
		private static Boolean CopyField(VehicleRecord v, String field, JsonElement val)
		{
			switch (field)
			{
				case "size_class":
					if (val.ValueKind == JsonValueKind.Number) { v.size_class = val.GetInt32(); return true; }
					return false;
				case "scm_speed": v.scm_speed = AsDouble(val); return v.scm_speed != null;
				case "boost_speed": v.boost_speed = AsDouble(val); return v.boost_speed != null;
				case "nav_speed": v.nav_speed = AsDouble(val); return v.nav_speed != null;
				case "mass": v.mass = AsDouble(val); return v.mass != null;
				case "mass_loadout": v.mass_loadout = AsDouble(val); return v.mass_loadout != null;
				case "mass_total": v.mass_total = AsDouble(val); return v.mass_total != null;
				case "hull_hp": v.hull_hp = AsDouble(val); return v.hull_hp != null;
				case "agility": v.agility = JsonRaw(val); return true;
				case "acceleration": v.acceleration = JsonRaw(val); return true;
				case "thrust_capacity": v.thrust_capacity = JsonRaw(val); return true;
				case "cross_section": v.cross_section = JsonRaw(val); return true;
			}
			return false;
		}

		private static Double? AsDouble(JsonElement v)
		{
			if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
			return null;
		}

		// Roundtrip the nested object through JsonDocument so System.Text.Json
		// re-serializes it cleanly with the rest of the output. Cloning the
		// element keeps the data alive past the source JsonDocument's Dispose().
		private static Object? JsonRaw(JsonElement v)
		{
			using var ms = new MemoryStream();
			using (var w = new Utf8JsonWriter(ms)) v.WriteTo(w);
			return JsonSerializer.Deserialize<JsonElement>(ms.ToArray());
		}
	}
}
