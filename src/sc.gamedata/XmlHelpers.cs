using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace sc.gamedata
{
	// Small helpers shared by every extractor. The unforge library returns
	// XmlElement nodes with attributes named exactly as they appear in CIG's
	// schema (CamelCase, mixed-case, etc.) so all access here is case-sensitive.
	internal static class XmlHelpers
	{
		public static String? Attr(XmlNode? node, String name)
		{
			if (node?.Attributes == null) return null;
			var a = node.Attributes[name];
			return a?.Value;
		}

		public static Double AttrDouble(XmlNode? node, String name, Double fallback = 0.0)
		{
			var s = Attr(node, name);
			if (String.IsNullOrEmpty(s)) return fallback;
			return Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
		}

		public static Double? AttrDoubleNullable(XmlNode? node, String name)
		{
			var s = Attr(node, name);
			if (String.IsNullOrEmpty(s)) return null;
			return Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (Double?)null;
		}

		public static Int32 AttrInt(XmlNode? node, String name, Int32 fallback = 0)
		{
			var s = Attr(node, name);
			if (String.IsNullOrEmpty(s)) return fallback;
			return Int32.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
		}

		public static Int32? AttrIntNullable(XmlNode? node, String name)
		{
			var s = Attr(node, name);
			if (String.IsNullOrEmpty(s)) return null;
			return Int32.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (Int32?)null;
		}

		// Pulls a per-axis damage profile from a DamageInfo-style element.
		public static DamageProfile DamageFrom(XmlNode? node)
		{
			return new DamageProfile
			{
				phys = AttrDouble(node, "DamagePhysical"),
				energy = AttrDouble(node, "DamageEnergy"),
				dist = AttrDouble(node, "DamageDistortion"),
				therm = AttrDouble(node, "DamageThermal"),
				bio = AttrDouble(node, "DamageBiochemical"),
				stun = AttrDouble(node, "DamageStun"),
			};
		}

		// Tag from an entity record path, e.g.
		// "libs/foundry/records/entities/scitem/ships/weapons/amrs_lasercannon_s1.xml"
		// → "AMRS_LaserCannon_S1" — taken from the root element's local name
		// after the leading "EntityClassDefinition." prefix.
		public static String EntityIdFromRoot(XmlElement root)
		{
			var name = root.LocalName;
			var dot = name.IndexOf('.');
			return dot >= 0 ? name.Substring(dot + 1) : name;
		}

		// Round to N decimal places — matches the rounding convention in
		// scripts/enrich_game_data.mjs so produced values line up with what
		// the LIVE pipeline emits.
		public static Double? Round(Double? value, Int32 decimals)
		{
			if (value == null) return null;
			return Math.Round(value.Value, decimals);
		}
	}

	// Locates an XmlElement by descendant tree-walk on local name. The
	// equivalent of Python ET's `.find(".//Tag")` — case-sensitive descendant
	// search returning the first match.
	internal static class XmlNav
	{
		public static XmlElement? FindFirst(XmlElement root, String localName)
		{
			foreach (var n in root.GetElementsByTagName(localName))
			{
				if (n is XmlElement e) return e;
			}
			return null;
		}

		public static IEnumerable<XmlElement> FindAll(XmlElement root, String localName)
		{
			foreach (var n in root.GetElementsByTagName(localName))
			{
				if (n is XmlElement e) yield return e;
			}
		}
	}

	// Name resolution: turn a `@LOC_KEY` reference into a real string via the
	// loaded localization map, falling back to a prettified version of the
	// entity id when the key is missing/placeholder.
	internal static class NameResolver
	{
		private static readonly Regex DesignatorRe = new(@"^[A-Z]+\d[A-Za-z0-9]*$", RegexOptions.Compiled);
		private static readonly Regex WeaponSizeRe = new(@"^S\d+$", RegexOptions.Compiled);
		private static readonly Regex MkArabicRe = new(@"\bMk(\d+)\b", RegexOptions.Compiled);
		private static readonly Dictionary<Int32, String> Roman = new()
		{
			[1] = "I", [2] = "II", [3] = "III", [4] = "IV", [5] = "V",
			[6] = "VI", [7] = "VII", [8] = "VIII", [9] = "IX", [10] = "X",
		};

		// Strip `{type_prefix}_` and any 4-letter manufacturer prefix
		// (`AEGS_`, `ORIG_`, `BEHR_`, …) so `ARMR_AEGS_Vanguard_Harbinger`
		// reduces to `Vanguard Harbinger`. Also runs CIG-specific tweaks
		// (`Mk1` → `Mk I`, designator-leading reorder for stranded codes).
		public static String PrettifyEntityId(String entityId, String typePrefix)
		{
			if (entityId.StartsWith(typePrefix, StringComparison.Ordinal))
				entityId = entityId.Substring(typePrefix.Length);
			var dot = entityId.IndexOf('_');
			if (dot > 0)
			{
				var head = entityId.Substring(0, dot);
				var rest = entityId.Substring(dot + 1);
				if (head.Length == 4 && IsAllUpperAlpha(head) && rest.Length > 0)
					entityId = rest;
			}
			var s = entityId.Replace('_', ' ').Trim();

			var parts = new List<String>(s.Split(' '));
			if (parts.Count >= 3 && !IsShipDesignator(parts[0]))
			{
				for (var i = 1; i < parts.Count - 1; i++)
				{
					if (IsShipDesignator(parts[i]))
					{
						var p = parts[i];
						parts.RemoveAt(i);
						parts.Insert(0, p);
						break;
					}
				}
			}
			s = String.Join(' ', parts);
			s = MkArabicRe.Replace(s, m =>
			{
				var n = Int32.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
				return Roman.TryGetValue(n, out var r) ? $"Mk {r}" : $"Mk {n}";
			});
			return s;
		}

		private static Boolean IsAllUpperAlpha(String s)
		{
			foreach (var c in s) if (!Char.IsUpper(c) || !Char.IsLetter(c)) return false;
			return true;
		}

		private static Boolean IsShipDesignator(String p)
			=> DesignatorRe.IsMatch(p) && !WeaponSizeRe.IsMatch(p);

		public static String Resolve(String? locKey, IDictionary<String, String> loc, String fallback, String entityId = "")
		{
			if (String.IsNullOrEmpty(locKey)) return fallback;
			var key = locKey.StartsWith('@') ? locKey.Substring(1) : locKey;
			// Guard against CIG copy-paste bugs: a few records reference an
			// unrelated entity's loc key. CIG also routinely re-orders or
			// renames tokens between an entity id and its loc key (entity
			// `BEHR_Nova_BallisticGatling_S5` → key
			// `item_NameBEHR_BallisticGatling_NOVA_S5`), and some entity ids
			// use placeholder prefixes like `NONE_` while their loc keys
			// reference the real manufacturer. So a strict substring check
			// of the full entity id rejects too many valid cases — instead,
			// require that ANY significant (length ≥ 3, not a pure
			// size-class designator) entity-id token appears somewhere in the
			// loc key. Truly unrelated keys (zero shared tokens) still fall
			// through to the prettified-id fallback.
			if (!String.IsNullOrEmpty(entityId) && !LocKeyMatchesEntity(key, entityId))
				return fallback;
			if (loc.TryGetValue(key, out var value) && !IsPlaceholder(value))
				return value;
			return fallback;
		}

		private static readonly Regex SizeClassRe = new(@"(?:^|_)S(\d+)(?:_|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		private static Boolean LocKeyMatchesEntity(String key, String entityId)
		{
			// First gate: if both entity id and key have a size-class token
			// (S1..S10), they must match. Catches CIG copy-paste bugs where an
			// S8/S9 entity reuses an S7 loc key (BEHR_LaserCannon_S8 →
			// @item_NameBEHR_LaserCannon_S7) — those would resolve to the
			// wrong-size weapon's display name otherwise.
			var entSize = SizeClassRe.Match(entityId);
			var keySize = SizeClassRe.Match(key);
			if (entSize.Success && keySize.Success && entSize.Groups[1].Value != keySize.Groups[1].Value)
				return false;

			// Second gate: at least one significant token (length ≥ 3, not a
			// size-class designator) from the entity id must appear in the
			// key. CIG re-orders or renames manufacturer/category tokens
			// between entity and loc key (entity `BEHR_Nova_BallisticGatling_S5`
			// → key `item_NameBEHR_BallisticGatling_NOVA_S5`), so we don't
			// require a contiguous substring match.
			var keyLower = key.ToLowerInvariant();
			foreach (var token in entityId.Split('_', StringSplitOptions.RemoveEmptyEntries))
			{
				if (token.Length < 3) continue;
				if (SizeClassRe.IsMatch("_" + token + "_")) continue; // skip S1, S2, …
				if (keyLower.IndexOf(token.ToLowerInvariant(), StringComparison.Ordinal) >= 0) return true;
			}
			return false;
		}

		private static Boolean IsPlaceholder(String v)
			=> String.IsNullOrEmpty(v)
			   || v.StartsWith("<=", StringComparison.Ordinal)
			   || v.StartsWith("@LOC_", StringComparison.Ordinal)
			   || v.EndsWith("UNINITIALIZED", StringComparison.Ordinal);
	}
}
