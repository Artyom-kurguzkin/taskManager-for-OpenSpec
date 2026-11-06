using System.Diagnostics;
using System.Text.RegularExpressions;
using WorkItems.BusinessRules;

namespace TaskManager.Tests;

/// <summary>What <see cref="DeltaSpec.Refresh"/> repairs so the OpenSpec CLI never refuses a delta (unit level).</summary>
public sealed class RepairingDeltaSpecsTests
{
    internal static string Req(string name, string text = "The system SHALL do it.") =>
        $"### Requirement: {name}\n{text}\n\n#### Scenario: {name} works\n- **WHEN** used\n- **THEN** it works\n";

    internal static string Main(params string[] names) =>
        "# Files Specification\n\n## Purpose\nStoring and sending files for the sample app the tests use, in detail.\n\n## Requirements\n\n" + string.Join("\n", names.Select(n => Req(n)));

    private static readonly string TwoMain = Main("Send money", "Export data");

    private static string Section(string kind, params string[] entries) => $"## {kind} Requirements\n\n" + string.Join("\n", entries);
    private static string Delta(params string[] sections) => string.Join("\n", sections);
    private static string Refresh(string delta, string main) => DeltaSpec.Refresh(delta, main);
    private static int Count(string text, string pattern) => Regex.Matches(text, pattern).Count;

    [Fact(DisplayName = "A modified requirement the main spec does not have is archived as an added one")]
    public void ModifiedNotInMainBecomesAdded()
    {
        string refreshed = Refresh(Delta(Section("MODIFIED", Req("Brand new"))), TwoMain);
        Assert.DoesNotContain("MODIFIED", refreshed);
        Assert.True(refreshed.IndexOf("## ADDED Requirements") < refreshed.IndexOf("### Requirement: Brand new"), refreshed);
    }

    [Fact(DisplayName = "Every modified requirement of a capability with no main spec yet is archived as an added one")]
    public void ModifiedWithoutAnyMainSpecBecomesAdded()
    {
        string refreshed = Refresh(Delta(Section("MODIFIED", Req("First"), Req("Second"))), "");
        Assert.DoesNotContain("MODIFIED", refreshed);
        Assert.Equal(1, Count(refreshed, "## ADDED Requirements"));
        Assert.Equal(1, Count(refreshed, "### Requirement: First"));
        Assert.Equal(1, Count(refreshed, "### Requirement: Second"));
    }

    [Fact(DisplayName = "A modified requirement spelled differently from the main spec's keeps its place as a modification, under the main spec's spelling")]
    public void ModifiedNearMissTakesMainsName()
    {
        string refreshed = Refresh(Delta(Section("MODIFIED", Req("send  MONEY"))), TwoMain);
        Assert.Contains("### Requirement: Send money\n", refreshed);
        Assert.DoesNotContain("Requirement: send  MONEY", refreshed);
        Assert.Contains("## MODIFIED Requirements", refreshed);
        Assert.DoesNotContain("ADDED", refreshed);
    }

    [Fact(DisplayName = "An added requirement that differs from a main-spec requirement only in case or spacing becomes a modification of it")]
    public void AddedNearMissBecomesModified()
    {
        string refreshed = Refresh(Delta(Section("ADDED", Req("EXPORT data"), Req("Other"))), TwoMain);
        Assert.Contains("## MODIFIED Requirements", refreshed);
        Assert.True(refreshed.IndexOf("### Requirement: Export data") > refreshed.IndexOf("## MODIFIED Requirements"), refreshed);
        Assert.True(refreshed.IndexOf("### Requirement: Other") > refreshed.IndexOf("## ADDED Requirements"), refreshed);
        Assert.DoesNotContain("Requirement: EXPORT data", refreshed);
    }

    [Fact(DisplayName = "A removed requirement spelled differently from the main spec's takes its spelling, and one the main spec lacks is dropped")]
    public void RemovedNearMissIsRewritten()
    {
        string refreshed = Refresh(Delta(Section("REMOVED", "### Requirement: SEND money\n**Reason**: gone\n", "### Requirement: Fax\n**Reason**: gone\n")), TwoMain);
        Assert.Contains("### Requirement: Send money\n", refreshed);
        Assert.DoesNotContain("SEND money", refreshed);
        Assert.DoesNotContain("Fax", refreshed);
    }

    [Fact(DisplayName = "A rename whose old name differs from the main spec's only in case takes the main spec's spelling")]
    public void RenameFromNearMissIsRewritten()
    {
        string refreshed = Refresh(Delta(Section("RENAMED", "- FROM: `### Requirement: send money`\n- TO: `### Requirement: Pay abroad`\n")), TwoMain);
        Assert.Contains("- FROM: `### Requirement: Send money`", refreshed);
        Assert.Contains("- TO: `### Requirement: Pay abroad`", refreshed);
    }

    [Fact(DisplayName = "A rename is dropped when neither name exists, when the new name is taken, when it is half written or repeated, or when there is no main spec")]
    public void DeadRenamesAreDropped()
    {
        const string ghost = "- FROM: `### Requirement: Ghost`\n- TO: `### Requirement: Phantom`\n";
        const string taken = "- FROM: `### Requirement: Send money`\n- TO: `### Requirement: Export data`\n";
        const string takenNear = "- FROM: `### Requirement: Send money`\n- TO: `### Requirement: EXPORT DATA`\n";
        Assert.DoesNotContain("RENAMED", Refresh(Delta(Section("RENAMED", ghost)), TwoMain));
        Assert.DoesNotContain("RENAMED", Refresh(Delta(Section("RENAMED", taken)), TwoMain));
        Assert.DoesNotContain("RENAMED", Refresh(Delta(Section("RENAMED", takenNear)), TwoMain));
        Assert.DoesNotContain("RENAMED", Refresh(Delta(Section("ADDED", Req("Keep me")), Section("RENAMED", taken)), ""));
        // a FROM with no TO, then a good pair: only the good pair stays; a TO with no FROM goes too
        string half = Refresh(Delta(Section("RENAMED", "- TO: `### Requirement: Orphan`\n- FROM: `### Requirement: Export data`\n- FROM: `### Requirement: Send money`\n- TO: `### Requirement: Pay abroad`\n- TO: `### Requirement: Extra`\n")), TwoMain);
        Assert.DoesNotContain("Orphan", half);
        Assert.DoesNotContain("Extra", half);
        Assert.DoesNotContain("`### Requirement: Export data`", half);
        Assert.Contains("- FROM: `### Requirement: Send money`\n- TO: `### Requirement: Pay abroad`", half);
        // a FROM or a TO used twice: the first pair stays
        string twice = Refresh(Delta(Section("RENAMED", "- FROM: `### Requirement: Send money`\n- TO: `### Requirement: A`\n- FROM: `### Requirement: Send money`\n- TO: `### Requirement: B`\n- FROM: `### Requirement: Export data`\n- TO: `### Requirement: A`\n")), TwoMain);
        Assert.Contains("TO: `### Requirement: A`", twice);
        Assert.Equal(1, Count(twice, "FROM:"));
        Assert.DoesNotContain("Requirement: B`", twice);
    }

    [Fact(DisplayName = "A modified requirement that still carries the old name of a rename is written under the new name")]
    public void ModifiedNamingRenameSourceTakesNewName()
    {
        string refreshed = Refresh(Delta(Section("RENAMED", "- FROM: `### Requirement: Send money`\n- TO: `### Requirement: Pay abroad`\n"), Section("MODIFIED", Req("Send money"))), TwoMain);
        Assert.Contains("### Requirement: Pay abroad\n", refreshed);
        Assert.Equal(1, Count(refreshed, "### Requirement: Send money"));   // only the rename's FROM line
        Assert.Contains("## MODIFIED Requirements", refreshed);
        // a rename whose source is missing leaves a modification of its target as an added requirement
        string gone = Refresh(Delta(Section("RENAMED", "- FROM: `### Requirement: Ghost`\n- TO: `### Requirement: Phantom`\n"), Section("MODIFIED", Req("Phantom"))), TwoMain);
        Assert.DoesNotContain("RENAMED", gone);
        Assert.True(gone.IndexOf("### Requirement: Phantom") > gone.IndexOf("## ADDED Requirements"), gone);
    }

    [Fact(DisplayName = "A removal of a requirement the same delta adds, modifies or renames is dropped")]
    public void RemovedAndWrittenIsDropped()
    {
        string refreshed = Refresh(Delta(
            Section("ADDED", Req("Fresh")),
            Section("MODIFIED", Req("Send money")),
            Section("REMOVED", "### Requirement: Fresh\n**Reason**: x\n", "### Requirement: Send money\n**Reason**: x\n", "### Requirement: Export data\n**Reason**: x\n"),
            Section("RENAMED", "- FROM: `### Requirement: Export data`\n- TO: `### Requirement: Dump data`\n")), TwoMain);
        Assert.DoesNotContain("REMOVED", refreshed);
        Assert.Equal(1, Count(refreshed, "### Requirement: Fresh"));
    }

    [Fact(DisplayName = "A requirement removed twice is removed once")]
    public void RemovedTwiceKeepsFirst()
    {
        string refreshed = Refresh(Delta(Section("REMOVED", "### Requirement: Send money\n**Reason**: first\n", "- `### Requirement: send money`\n", "### Requirement: Export data\n**Reason**: other\n")), TwoMain);
        Assert.Equal(1, Count(refreshed, "(?i)Requirement: send money"));
        Assert.Contains("first", refreshed);
        Assert.Contains("### Requirement: Export data", refreshed);
    }

    [Fact(DisplayName = "Requirements above the first section, or in a delta with no sections at all, are archived as added")]
    public void LooseRequirementsGoUnderAdded()
    {
        string bare = Refresh(Req("Alpha") + "\n" + Req("Beta"), "");
        Assert.StartsWith("## ADDED Requirements", bare);
        Assert.Equal(1, Count(bare, "### Requirement: Alpha"));
        Assert.Equal(1, Count(bare, "### Requirement: Beta"));
        // the main spec has it: it is a modification
        string known = Refresh(Req("Send money") + "\n" + Req("Beta"), TwoMain);
        Assert.True(known.IndexOf("### Requirement: Send money") > known.IndexOf("## MODIFIED Requirements"), known);
        Assert.True(known.IndexOf("### Requirement: Beta") > known.IndexOf("## ADDED Requirements"), known);
        // above a section that exists
        string above = Refresh("Some intro.\n\n" + Req("Alpha") + "\n" + Section("REMOVED", "### Requirement: Send money\n**Reason**: x\n"), TwoMain);
        Assert.StartsWith("Some intro.", above);
        Assert.True(above.IndexOf("## ADDED Requirements") < above.IndexOf("### Requirement: Alpha"), above);
        Assert.True(above.IndexOf("### Requirement: Alpha") < above.IndexOf("## REMOVED Requirements"), above);
    }

    [Fact(DisplayName = "Section headings are read in any case and with any spacing, and come out canonical once the file is rewritten")]
    public void HeadingsAreCanonical()
    {
        string odd = "##   added   REQUIREMENTS\n\n" + Req("Send money");
        string refreshed = Refresh(odd, TwoMain);
        Assert.Contains("## MODIFIED Requirements", refreshed);
        Assert.DoesNotContain("added", refreshed);
        // a heading the CLI reads as it stands (only the case differs) is not touched
        string lower = "## added requirements\n\n" + Req("Fresh");
        Assert.Equal(lower, Refresh(lower, TwoMain));
        // but a mixed-case one is made canonical when something else needs fixing
        string mixed = Refresh("## Added Requirements\n\n" + Req("Fresh") + "\n## Removed Requirements\n\n### Requirement: Fax\n**Reason**: x\n\n## Modified Requirements\n\n" + Req("Ghost"), TwoMain);
        Assert.DoesNotContain("Added", mixed);
        Assert.DoesNotContain("Modified", mixed);
        Assert.DoesNotContain("Removed", mixed);
    }

    public static IEnumerable<object[]> Broken() =>
    [
        [Delta(Section("MODIFIED", Req("Brand new")))],
        [Delta(Section("MODIFIED", Req("send MONEY")), Section("ADDED", Req("export DATA")))],
        [Delta(Section("REMOVED", "### Requirement: SEND money\n**Reason**: x\n", "### Requirement: Fax\n**Reason**: x\n"))],
        [Delta(Section("RENAMED", "- FROM: `### Requirement: send money`\n- TO: `### Requirement: Pay abroad`\n"), Section("MODIFIED", Req("Send money")))],
        [Req("Alpha") + "\n" + Req("Send money")],
        ["##  Added  Requirements\n\n" + Req("Alpha") + "\n" + Req("Alpha")],
        [Delta(Section("RENAMED", "- FROM: `### Requirement: Ghost`\n- TO: `### Requirement: Phantom`\n"), Section("MODIFIED", Req("Phantom")))],
    ];

    [Theory(DisplayName = "Refreshing a repaired delta again changes nothing, whatever the line endings, and never leaves three blank lines in a row")]
    [MemberData(nameof(Broken))]
    public void RefreshIsIdempotent(string delta)
    {
        foreach (string main in new[] { TwoMain, "" })
        {
            string once = Refresh(delta, main);
            Assert.Equal(once, Refresh(once, main));
            Assert.DoesNotContain("\n\n\n", once);
            string crlf = Refresh(delta.Replace("\n", "\r\n"), main);
            Assert.Equal(once.Replace("\n", "\r\n"), crlf);
            Assert.DoesNotContain("\r\n\r\n\r\n", crlf);
            Assert.Equal(crlf, Refresh(crlf, main));
        }
    }

    [Fact(DisplayName = "A delta with nothing wrong comes back exactly as it was")]
    public void CleanDeltaIsUntouched()
    {
        string clean = Delta(
            Section("RENAMED", "- FROM: `### Requirement: Send money`\n- TO: `### Requirement: Pay abroad`\n"),
            Section("REMOVED", "### Requirement: Export data\n**Reason**: gone\n"),
            Section("MODIFIED", Req("Pay abroad")),
            Section("ADDED", Req("Fresh")));
        Assert.Equal(clean, Refresh(clean, TwoMain));
        Assert.Equal(clean.Replace("\n", "\r\n"), Refresh(clean.Replace("\n", "\r\n"), TwoMain));
    }
}

/// <summary>The same repairs against the real OpenSpec CLI: each delta below is refused as written and archives once refreshed.</summary>
[Trait("Category", "OpenSpecCli")]
public sealed class ArchivingRepairedDeltaSpecsTests
{
    private const string Cap = "files";

    private static (int Exit, string Output) OpenSpec(string root, string args)
    {
        ProcessStartInfo psi = OperatingSystem.IsWindows()
            ? new("cmd.exe", $"/c npx -y @fission-ai/openspec@1.14.3 {args}")
            : new("/bin/sh", $"-c \"npx -y @fission-ai/openspec@1.14.3 {args}\"");
        psi.WorkingDirectory = root;
        psi.RedirectStandardOutput = psi.RedirectStandardError = true;
        using Process p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    /// <summary>Builds the folder, refreshes the delta, archives it with the CLI and returns the main spec afterwards.</summary>
    private static string Archive(string? main, string brokenDelta)
    {
        string root = Directory.CreateTempSubdirectory("rtm-delta").FullName;
        try
        {
            void Write(string relative, string text)
            {
                string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
            }
            Directory.CreateDirectory(Path.Combine(root, "openspec", "specs"));
            if (main is not null) Write($"openspec/specs/{Cap}/spec.md", main);
            Write("openspec/changes/repair/.openspec.yaml", "schema: spec-driven\ncreated: 2026-11-06\n");
            Write("openspec/changes/repair/proposal.md", "## Why\nThe delta spec was written by hand and needs to archive without errors.\n\n## What Changes\n- Files\n");
            Write("openspec/changes/repair/tasks.md", "## 1. Work\n- [x] 1.1 Done\n");
            Write($"openspec/changes/repair/specs/{Cap}/spec.md", DeltaSpec.Refresh(brokenDelta, main ?? ""));

            (int exit, string output) = OpenSpec(root, "archive repair --yes");

            if (exit != 0) Assert.Fail("openspec archive failed:" + Environment.NewLine + output);
            return File.ReadAllText(Path.Combine(root, "openspec", "specs", Cap, "spec.md"));
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    private static string Req(string name, string text = "The system SHALL do it.") => RepairingDeltaSpecsTests.Req(name, text);
    private static string Section(string kind, params string[] entries) => $"## {kind} Requirements\n\n" + string.Join("\n", entries);

    [Fact(DisplayName = "A modified requirement in a capability with no main spec archives through the real CLI and ends up in the new main spec")]
    public void ModifiedWithNoMainSpecArchives()
    {
        string main = Archive(null, Section("MODIFIED", Req("Brand new", "The system SHALL be brand new.")));
        Assert.Contains("### Requirement: Brand new", main);
    }

    [Fact(DisplayName = "A modified requirement the existing main spec lacks archives through the real CLI as an added one")]
    public void ModifiedMissingFromMainArchives()
    {
        string main = Archive(RepairingDeltaSpecsTests.Main("Existing"), Section("MODIFIED", Req("Brand new")));
        Assert.Contains("### Requirement: Brand new", main);
        Assert.Contains("### Requirement: Existing", main);
    }

    [Fact(DisplayName = "An added requirement that differs from an existing one only in case archives through the real CLI as a modification")]
    public void AddedNearMissArchives()
    {
        string main = Archive(RepairingDeltaSpecsTests.Main("Existing"), Section("ADDED", Req("EXISTING", "The system SHALL be changed.")));
        Assert.Contains("The system SHALL be changed.", main);
        Assert.Single(Regex.Matches(main, "(?i)### Requirement: existing"));
    }

    [Fact(DisplayName = "Requirements with no section heading archive through the real CLI")]
    public void NoSectionHeadingArchives()
    {
        string main = Archive(RepairingDeltaSpecsTests.Main("Existing"), Req("Loose one") + "\n" + Req("Existing", "The system SHALL be changed."));
        Assert.Contains("### Requirement: Loose one", main);
        Assert.Contains("The system SHALL be changed.", main);
    }

    [Fact(DisplayName = "A rename of a requirement that does not exist, with a modification of its new name, archives through the real CLI")]
    public void RenameOfMissingSourceArchives()
    {
        string main = Archive(RepairingDeltaSpecsTests.Main("Existing"),
            Section("RENAMED", "- FROM: `### Requirement: Ghost`\n- TO: `### Requirement: Phantom`\n") + "\n" + Section("MODIFIED", Req("Phantom")));
        Assert.Contains("### Requirement: Phantom", main);
        Assert.DoesNotContain("Ghost", main);
    }
}
