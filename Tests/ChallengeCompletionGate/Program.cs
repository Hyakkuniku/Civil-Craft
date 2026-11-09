using System.Collections;

int checks = 0;
void Check(bool condition, string description)
{ checks++; if (!condition) throw new InvalidOperationException(description); }
void Throws<T>(Action action, string description) where T : Exception
{
    try { action(); }
    catch (T) { checks++; return; }
    throw new InvalidOperationException(description);
}

var input = new List<string> { " C ", "A", "B", "A" };
var catalog = ChallengeCompletionCatalog.Create(input);
Check(catalog.Count == 3 && catalog.Ids.SequenceEqual(new[] { "A", "B", "C" }), "Manifest order and duplicates must be canonical.");
var reordered = ChallengeCompletionCatalog.Create(new[] { "B", "C", " A " });
Check(catalog.Fingerprint == reordered.Fingerprint && catalog.Fingerprint.Length == 64, "Different enumeration order must not change the shared manifest.");
Check(catalog.Fingerprint != ChallengeCompletionCatalog.Create(new[] { "A", "B" }).Fingerprint, "A changed contract set must change its signature.");
Check(ChallengeCompletionCatalog.Create(new[] { "A", "a" }).Count == 2, "Permanent IDs remain ordinal and case-sensitive.");
input[0] = "REPLACED";
Check(catalog.Ids.SequenceEqual(new[] { "A", "B", "C" }), "The manifest must not retain mutable caller storage.");
Throws<NotSupportedException>(() => ((IList<string>)catalog.Ids)[0] = "MUTATED", "Callers must not mutate the manifest through another interface.");
Throws<ArgumentNullException>(() => ChallengeCompletionCatalog.Create(null), "A missing manifest must reject.");
foreach (string invalid in new[] { "", "  ", "A\nB", "\tA", "A\0", new string('X', 129), null })
    Throws<ArgumentException>(() => ChallengeCompletionCatalog.Create(new[] { invalid }), "Invalid permanent ID must reject.");
Check(ChallengeCompletionCatalog.Create(new[] { new string('X', 128) }).Count == 1, "The maximum valid ID length must be accepted.");
Check(ChallengeCompletionCatalog.Create(new[] { "Brücke", "橋", "🏗" }).Count == 3, "Valid Unicode permanent IDs must remain supported.");
Throws<ArgumentException>(() => ChallengeCompletionCatalog.Create(Enumerable.Range(0, 129).Select(i => "contract_" + i)), "Manifest overflow must reject rather than drop progress.");

var hostCompleted = new HashSet<string> { "A", "C" };
var guestCompleted = new HashSet<string> { "B", "C" };
catalog.Capture(hostCompleted.Contains, out ulong hostLow, out ulong hostHigh);
catalog.Capture(guestCompleted.Contains, out ulong guestLow, out ulong guestHigh);
bool Shared(string id, bool hostReady = true, bool guestReady = true, string guestFingerprint = null) =>
    catalog.HasCompletion(id, catalog.Fingerprint, hostReady, hostLow, hostHigh) &&
    catalog.HasCompletion(id, guestFingerprint ?? catalog.Fingerprint, guestReady, guestLow, guestHigh);
Check(!Shared("A") && !Shared("B") && Shared("C"), "Only the intersection of personal completions may be eligible.");
Check(!Shared("C", hostReady: false) && !Shared("C", guestReady: false), "An unready participant must fail closed.");
Check(!Shared("C", guestFingerprint: reordered.Fingerprint + "bad"), "A mismatched client catalog must fail closed.");
Check(!Shared("unknown"), "An unknown contract must not borrow another completion bit.");
Check(catalog.HasCompletion(" C ", catalog.Fingerprint, true, hostLow, hostHigh), "Canonical whitespace should resolve the existing permanent ID.");
Check(!catalog.HasCompletion("C", null, true, hostLow, hostHigh), "A missing fingerprint must fail closed.");
Check(!catalog.HasCompletion("C", catalog.Fingerprint.ToUpperInvariant(), true, hostLow, hostHigh), "Fingerprint matching must be exact.");
Check(!catalog.HasCompletion("C", catalog.Fingerprint, true, hostLow | (1UL << 40), hostHigh), "Invalid extra low bits must reject the entire snapshot.");
Check(!catalog.HasCompletion("C", catalog.Fingerprint, true, hostLow, 1), "Invalid extra high bits must reject the entire snapshot.");
Check(!catalog.HasCompletion("C\n", catalog.Fingerprint, true, hostLow, hostHigh), "Malformed lookup IDs must fail closed.");
guestCompleted.Remove("C"); catalog.Capture(guestCompleted.Contains, out guestLow, out guestHigh);
Check(!Shared("C"), "A refreshed snapshot that loses completion must remove shared eligibility.");
guestCompleted.Add("C"); catalog.Capture(guestCompleted.Contains, out guestLow, out guestHigh);
Check(Shared("C"), "A restored personal completion may re-enter the intersection.");
Check(hostCompleted.SetEquals(new[] { "A", "C" }) && guestCompleted.SetEquals(new[] { "B", "C" }), "Capturing eligibility must never change a personal save source.");
Throws<ArgumentNullException>(() => catalog.Capture(null, out _, out _), "A missing personal-completion reader must reject.");

string[] fullIds = Enumerable.Range(0, 128).Select(i => "contract_" + i.ToString("D3")).ToArray();
var full = ChallengeCompletionCatalog.Create(fullIds);
full.Capture(id => id == "contract_063" || id == "contract_064" || id == "contract_127", out ulong low, out ulong high);
Check(low == 0x8000000000000000UL && high == 0x8000000000000001UL, "The two words must encode the 63/64/127 boundaries without wrapping.");
foreach (int index in new[] { 0, 62, 63, 64, 65, 126, 127 })
    Check(full.HasCompletion(fullIds[index], full.Fingerprint, true, low, high) == (index == 63 || index == 64 || index == 127),
        "Boundary lookup mismatch at " + index);
Check(full.ValidBits(ulong.MaxValue, ulong.MaxValue), "Every bit is valid in a full 128-contract catalog.");
foreach (int count in new[] { 0, 1, 2, 63, 64, 65, 127, 128 })
{
    var bounded = ChallengeCompletionCatalog.Create(fullIds.Take(count));
    bounded.Capture(_ => true, out ulong allLow, out ulong allHigh);
    Check(bounded.ValidBits(allLow, allHigh), "A valid captured snapshot must pass at count " + count);
    Check(bounded.Ids.All(id => bounded.HasCompletion(id, bounded.Fingerprint, true, allLow, allHigh)), "All completed IDs must resolve at count " + count);
    if (count < 64) Check(!bounded.ValidBits(allLow | (1UL << count), allHigh), "The first unassigned low bit must reject at count " + count);
    else if (count < 128) Check(!bounded.ValidBits(allLow, allHigh | (1UL << (count - 64))), "The first unassigned high bit must reject at count " + count);
}
var empty = ChallengeCompletionCatalog.Create(Array.Empty<string>());
Check(empty.Count == 0 && empty.ValidBits(0, 0) && !empty.HasCompletion("A", empty.Fingerprint, true, 0, 0), "An empty manifest cannot grant completion.");
Check(ChallengeCompletionCatalog.Create(new[] { "AB", "C" }).Fingerprint != ChallengeCompletionCatalog.Create(new[] { "A", "BC" }).Fingerprint,
    "The manifest signature must preserve ID boundaries.");

Console.WriteLine("PASS: " + checks + " challenge-completion catalog and both-player intersection assertions.");
