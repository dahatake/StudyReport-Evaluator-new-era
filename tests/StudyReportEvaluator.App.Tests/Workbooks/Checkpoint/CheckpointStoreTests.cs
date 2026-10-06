using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.Tests.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workbooks.Checkpoint;

// Requirements: FR-030 (AC-031)
public sealed class CheckpointStoreTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("long-context")]
    public void Context_tier_round_trips_and_cannot_change_during_append(string? tier)
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        CheckpointEnvelope envelope = CreateEnvelope(input) with { ContextTier = tier };
        CheckpointStore store = new();
        Assert.True(store.Create(envelope, TestContext.Current.CancellationToken).IsSuccess);
        Assert.Equal(tier, store.Load(envelope.PartialPath, TestContext.Current.CancellationToken).Envelope!.ContextTier);
        byte[] before = File.ReadAllBytes(envelope.PartialPath);
        Assert.False(store.Update(envelope with { ContextTier = tier is null ? "long-context" : null },
            TestContext.Current.CancellationToken).IsSuccess);
        Assert.Equal(before, File.ReadAllBytes(envelope.PartialPath));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("invalid")]
    [InlineData("")]
    public void Invalid_or_noncanonical_checkpoint_context_tier_is_rejected_before_writes(string tier)
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        CheckpointEnvelope envelope = CreateEnvelope(input) with { ContextTier = tier };
        Assert.False(new CheckpointStore().Create(envelope, TestContext.Current.CancellationToken).IsSuccess);
        Assert.False(File.Exists(envelope.PartialPath));
    }

    private static readonly DateTimeOffset StartedAtUtc =
        new(2026, 9, 2, 5, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Create_and_read_round_trip_preserves_input_and_writes_only_a_closed_checkpoint_sheet()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        InputSnapshot original = new InputSnapshotService().Capture(input.Path);
        string[] originalSheetNames = SheetNames(input.Path);
        string originalWorksheetHash = WorksheetHash(input.Path, "Original");
        CheckpointEnvelope envelope = CreateEnvelope(input);
        CheckpointStore store = new();

        CheckpointSaveResult saved = store.Create(envelope, TestContext.Current.CancellationToken);
        CheckpointLoadResult loaded = store.Load(envelope.PartialPath, TestContext.Current.CancellationToken);

        Assert.True(saved.IsSuccess, saved.ToString());
        Assert.True(saved.CreatedNew);
        Assert.True(loaded.IsSuccess, loaded.ToString());
        CheckpointEnvelope actual = Assert.IsType<CheckpointEnvelope>(loaded.Envelope);
        Assert.Equal(envelope.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(envelope.InputPath, actual.InputPath);
        Assert.Equal(envelope.Input, actual.Input);
        Assert.Equal(envelope.DefinitionCanonicalJson, actual.DefinitionCanonicalJson);
        Assert.Equal(envelope.DefinitionSha256, actual.DefinitionSha256);
        Assert.Equal(envelope.NormalModelId, actual.NormalModelId);
        Assert.Equal(envelope.ReferenceModelId, actual.ReferenceModelId);
        Assert.Equal(envelope.FinalPath, actual.FinalPath);
        Assert.Equal(envelope.PartialPath, actual.PartialPath);
        Assert.Empty(actual.References);
        Assert.Empty(actual.CompletedRows);
        Assert.True(new InputSnapshotService().Recheck(input.Path, original).IsMatch);
        Assert.Equal(originalWorksheetHash, WorksheetHash(envelope.PartialPath, "Original"));
        using SpreadsheetDocument partial = SpreadsheetDocument.Open(envelope.PartialPath, false);
        Workbook workbook = partial.WorkbookPart?.Workbook
            ?? throw new InvalidDataException("The partial workbook root is missing.");
        Assert.Equal(originalSheetNames.Append(CheckpointStore.CheckpointSheetName),
            workbook.Descendants<Sheet>().Select(sheet => sheet.Name?.Value));
        Assert.Empty(new OpenXmlValidator().Validate(partial, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(envelope.PartialPath)!,
            ".study-report-evaluator-*.checkpoint.xlsx"));
        Assert.Contains("<redacted>", envelope.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(envelope.InputPath, envelope.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(envelope.PartialPath, loaded.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Payload_is_split_into_ordered_chunks_no_larger_than_thirty_thousand_characters()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        string firstAnswer = new('あ', 32_767);
        string secondAnswer = new('い', 32_767);
        CheckpointEnvelope envelope = CreateEnvelope(input) with
        {
            References =
            [
                Reference("Q1", firstAnswer, StartedAtUtc.AddSeconds(10)),
                Reference("Q2", secondAnswer, StartedAtUtc.AddSeconds(20)),
            ],
        };
        CheckpointStore store = new();

        Assert.True(store.Create(envelope, TestContext.Current.CancellationToken).IsSuccess);

        using SpreadsheetDocument document = SpreadsheetDocument.Open(envelope.PartialPath, false);
        Worksheet worksheet = CheckpointWorksheet(document);
        Row[] payloadRows = worksheet.Descendants<Row>()
            .Where(row => Inline(Cell(row, "A" + row.RowIndex!.Value)) == "PAYLOAD")
            .ToArray();
        Assert.True(payloadRows.Length >= 3);
        Assert.Equal(
            Enumerable.Range(0, payloadRows.Length).Select(index => index.ToString(CultureInfo.InvariantCulture)),
            payloadRows.Select(row => Cell(row, "B" + row.RowIndex!.Value).CellValue?.Text));
        Assert.All(payloadRows, row => Assert.InRange(
            Inline(Cell(row, "C" + row.RowIndex!.Value)).Length,
            1,
            30_000));
        Assert.True(store.Load(envelope.PartialPath, TestContext.Current.CancellationToken).IsSuccess);
    }

    [Fact]
    public void Completed_row_round_trips_normal_special_similarity_and_usage_as_one_durable_unit()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        CheckpointEnvelope envelope = CreateEnvelope(input) with
        {
            CompletedRows =
            [
                new CheckpointCompletedRow
                {
                    SourceRowNumber = 2,
                    NormalResults =
                    [
                        new CheckpointNormalResult
                        {
                            QuestionId = "Q1",
                            EvaluatorId = "E1",
                            StatusCode = ResultsStatusCodes.Success,
                            AttemptCount = 1,
                            Scorable = true,
                            ScorableKnown = true,
                            AcceptedResult = new QuantificationResult
                            {
                                EvaluatorId = "E1",
                                Criteria =
                                [
                                    new CriterionQuantificationResult
                                    {
                                        CriterionId = "C1",
                                        RawScore = 5m,
                                        Reason = "normal reason",
                                        Evidence = "normal evidence",
                                        EvidenceSource = EvidenceSourceKind.PrimaryAnswer,
                                        EvidenceSourceColumnId = "A",
                                    },
                                ],
                            },
                            TokenUsage = Usage(),
                        },
                    ],
                    SpecialResults =
                    [
                        new CheckpointSpecialResult
                        {
                            QuestionId = "Q1",
                            SpecialEvaluationId = "S1",
                            StatusCode = ResultsStatusCodes.Success,
                            AttemptCount = 1,
                            AcceptedResult = new SpecialQuantificationResult
                            {
                                SpecialEvaluationId = "S1",
                                Score = 0.8m,
                                Reason = "special reason",
                                Evidence = "special evidence",
                                EvidenceSource = EvidenceSourceKind.SupportingColumn,
                                EvidenceSourceColumnId = "B",
                            },
                            TokenUsage = Usage(),
                        },
                    ],
                    SimilarityResults =
                    [
                        new CheckpointSimilarityResult
                        {
                            QuestionId = "Q1",
                            StatusCode = ResultsStatusCodes.Success,
                            AttemptCount = 1,
                            AcceptedResult = new SimilarityQuantificationResult
                            {
                                QuestionId = "Q1",
                                Similarity = 0.4m,
                                Reason = "similarity reason",
                            },
                            TokenUsage = Usage(),
                        },
                    ],
                },
            ],
        };
        CheckpointStore store = new();

        Assert.True(store.Create(envelope, TestContext.Current.CancellationToken).IsSuccess);
        CheckpointEnvelope loaded = Assert.IsType<CheckpointEnvelope>(store.Load(
            envelope.PartialPath,
            TestContext.Current.CancellationToken).Envelope);

        CheckpointCompletedRow row = Assert.Single(loaded.CompletedRows);
        CheckpointNormalResult normal = Assert.Single(row.NormalResults);
        CheckpointSpecialResult special = Assert.Single(row.SpecialResults);
        CheckpointSimilarityResult similarity = Assert.Single(row.SimilarityResults);
        Assert.Equal(5m, Assert.Single(normal.AcceptedResult!.Criteria).RawScore);
        Assert.Equal(EvidenceSourceKind.PrimaryAnswer, Assert.Single(normal.AcceptedResult.Criteria).EvidenceSource);
        Assert.Equal(0.8m, special.AcceptedResult!.Score);
        Assert.Equal(EvidenceSourceKind.SupportingColumn, special.AcceptedResult.EvidenceSource);
        Assert.Equal(0.4m, similarity.AcceptedResult!.Similarity);
        Assert.Equal(10, normal.TokenUsage.InputTokens);
        Assert.True(normal.TokenUsage.IsAvailable);
    }

    [Fact]
    public void Payload_hash_mismatch_is_rejected_without_echoing_payload_content()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        CheckpointEnvelope envelope = CreateEnvelope(input);
        CheckpointStore store = new();
        Assert.True(store.Create(envelope, TestContext.Current.CancellationToken).IsSuccess);
        const string privateCanary = "PRIVATE-HASH-MUTATION-CANARY";
        Mutate(envelope.PartialPath, worksheet =>
        {
            Cell payload = Cell(worksheet.Descendants<Row>().Single(row => row.RowIndex?.Value == 5), "C5");
            SetInline(payload, Inline(payload) + privateCanary);
        });

        CheckpointLoadResult result = store.Load(envelope.PartialPath, TestContext.Current.CancellationToken);

        Assert.Equal(CheckpointStatusCodes.HashMismatch, result.Code);
        Assert.Null(result.Envelope);
        Assert.DoesNotContain(privateCanary, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Load_rejects_a_partial_whose_preserved_source_sheet_was_modified()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        CheckpointEnvelope envelope = CreateEnvelope(input);
        CheckpointStore store = new();
        Assert.True(store.Create(envelope, TestContext.Current.CancellationToken).IsSuccess);
        const string privateCanary = "PRIVATE-PRESERVED-SHEET-MUTATION";
        using (SpreadsheetDocument document = SpreadsheetDocument.Open(envelope.PartialPath, true))
        {
            WorkbookPart workbookPart = document.WorkbookPart
                ?? throw new InvalidDataException("Synthetic workbook part is missing.");
            Workbook workbook = workbookPart.Workbook
                ?? throw new InvalidDataException("Synthetic workbook root is missing.");
            Sheet original = workbook.Descendants<Sheet>().Single(sheet => sheet.Name?.Value == "Original");
            Worksheet worksheet = ((WorksheetPart)workbookPart.GetPartById(original.Id!.Value!)).Worksheet
                ?? throw new InvalidDataException("Synthetic worksheet is missing.");
            SheetData data = worksheet.GetFirstChild<SheetData>()
                ?? throw new InvalidDataException("Synthetic sheet data is missing.");
            Row row = data.Elements<Row>().First();
            row.Append(InlineCell("D1", privateCanary));
            worksheet.Save();
        }

        CheckpointLoadResult result = store.Load(
            envelope.PartialPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(CheckpointStatusCodes.Invalid, result.Code);
        Assert.Null(result.Envelope);
        Assert.DoesNotContain(privateCanary, result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unknown-property")]
    [InlineData("missing-required-property")]
    public void Closed_canonical_json_rejects_unknown_or_missing_properties(string mutation)
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        CheckpointEnvelope envelope = CreateEnvelope(input);
        CheckpointStore store = new();
        Assert.True(store.Create(envelope, TestContext.Current.CancellationToken).IsSuccess);
        RewriteSingleChunk(envelope.PartialPath, payload =>
        {
            JsonObject root = JsonNode.Parse(payload)?.AsObject()
                ?? throw new InvalidDataException("Synthetic checkpoint JSON is missing.");
            if (mutation == "unknown-property")
            {
                root["privateUnknownCanary"] = "PRIVATE-UNKNOWN-JSON-CANARY";
            }
            else
            {
                Assert.True(root.Remove("normalModelId"));
            }

            return root.ToJsonString();
        });

        CheckpointLoadResult result = store.Load(envelope.PartialPath, TestContext.Current.CancellationToken);

        Assert.Equal(CheckpointStatusCodes.Invalid, result.Code);
        Assert.Null(result.Envelope);
        Assert.DoesNotContain("PRIVATE-UNKNOWN-JSON-CANARY", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_sheet_schema_has_a_specific_safe_status()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        CheckpointEnvelope envelope = CreateEnvelope(input);
        CheckpointStore store = new();
        Assert.True(store.Create(envelope, TestContext.Current.CancellationToken).IsSuccess);
        Mutate(envelope.PartialPath, worksheet => SetInline(
            Cell(worksheet.Descendants<Row>().Single(row => row.RowIndex?.Value == 2), "C2"),
            "1"));

        CheckpointLoadResult result = store.Load(envelope.PartialPath, TestContext.Current.CancellationToken);

        Assert.Equal(CheckpointStatusCodes.SchemaUnsupported, result.Code);
        Assert.Null(result.Envelope);
    }

    [Theory]
    [InlineData("unknown-record")]
    [InlineData("unknown-column")]
    [InlineData("missing-chunk")]
    [InlineData("duplicate-chunk")]
    [InlineData("index-gap")]
    [InlineData("duplicate-meta")]
    public void Worksheet_reader_rejects_unknown_missing_duplicate_or_gapped_content(string mutation)
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        CheckpointEnvelope envelope = CreateEnvelope(input) with
        {
            References =
            [
                Reference("Q1", new string('A', 32_767), StartedAtUtc.AddSeconds(10)),
                Reference("Q2", new string('B', 32_767), StartedAtUtc.AddSeconds(20)),
            ],
        };
        CheckpointStore store = new();
        Assert.True(store.Create(envelope, TestContext.Current.CancellationToken).IsSuccess);
        Mutate(envelope.PartialPath, worksheet =>
        {
            SheetData data = worksheet.GetFirstChild<SheetData>()
                ?? throw new InvalidDataException("Synthetic sheet data is missing.");
            Row firstPayload = data.Elements<Row>().Single(row => row.RowIndex?.Value == 5);
            switch (mutation)
            {
                case "unknown-record":
                    SetInline(Cell(firstPayload, "A5"), "UNKNOWN");
                    break;
                case "unknown-column":
                    firstPayload.Append(InlineCell("D5", "PRIVATE-UNKNOWN-COLUMN"));
                    break;
                case "missing-chunk":
                    data.RemoveChild(data.Elements<Row>().Last());
                    break;
                case "duplicate-chunk":
                    Cell(data.Elements<Row>().Single(row => row.RowIndex?.Value == 6), "B6").CellValue = new CellValue("0");
                    break;
                case "index-gap":
                    Cell(firstPayload, "B5").CellValue = new CellValue("1");
                    break;
                case "duplicate-meta":
                    SetInline(Cell(data.Elements<Row>().Single(row => row.RowIndex?.Value == 4), "B4"), "PayloadSha256");
                    break;
                default:
                    throw new InvalidOperationException("Unknown synthetic mutation.");
            }
        });

        CheckpointLoadResult result = store.Load(envelope.PartialPath, TestContext.Current.CancellationToken);

        Assert.Equal(CheckpointStatusCodes.Invalid, result.Code);
        Assert.Null(result.Envelope);
        Assert.DoesNotContain("PRIVATE-UNKNOWN-COLUMN", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Physical_update_atomically_advances_reference_state_and_keeps_one_partial()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        CheckpointEnvelope initial = CreateEnvelope(input);
        CheckpointStore store = new();
        Assert.True(store.Create(initial, TestContext.Current.CancellationToken).IsSuccess);
        CheckpointEnvelope next = initial with
        {
            SavedAtUtc = initial.SavedAtUtc.AddMinutes(1),
            References = [Reference("Q1", "reference answer", StartedAtUtc.AddSeconds(30))],
        };

        CheckpointSaveResult updated = store.Update(next, TestContext.Current.CancellationToken);
        CheckpointLoadResult loaded = store.Load(next.PartialPath, TestContext.Current.CancellationToken);

        Assert.True(updated.IsSuccess, updated.ToString());
        Assert.False(updated.CreatedNew);
        CheckpointReference reference = Assert.Single(Assert.IsType<CheckpointEnvelope>(loaded.Envelope).References);
        Assert.Equal("Q1", reference.QuestionId);
        Assert.Equal("reference answer", reference.Answer);
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(next.PartialPath)!,
            ".study-report-evaluator-*.checkpoint.xlsx"));
    }

    [Fact]
    public void Replace_failure_preserves_the_previous_valid_partial_and_cleans_temp()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        CheckpointEnvelope initial = CreateEnvelope(input);
        TestCheckpointFileOperations operations = new();
        CheckpointStore store = new(operations);
        Assert.True(store.Create(initial, TestContext.Current.CancellationToken).IsSuccess);
        string oldBytesHash = FileHash(initial.PartialPath);
        CheckpointEnvelope next = initial with
        {
            SavedAtUtc = initial.SavedAtUtc.AddMinutes(1),
            References = [Reference("Q1", "new private answer", StartedAtUtc.AddSeconds(30))],
        };
        operations.ReplaceOverride = (_, _) => throw new IOException("PRIVATE-REPLACE-FAULT-CANARY");

        CheckpointSaveResult result = store.Update(next, TestContext.Current.CancellationToken);

        Assert.Equal(CheckpointStatusCodes.SaveFailed, result.Code);
        Assert.Equal(oldBytesHash, FileHash(initial.PartialPath));
        CheckpointEnvelope retained = Assert.IsType<CheckpointEnvelope>(store.Load(
            initial.PartialPath,
            TestContext.Current.CancellationToken).Envelope);
        Assert.Empty(retained.References);
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(initial.PartialPath)!,
            ".study-report-evaluator-*.checkpoint.xlsx"));
        Assert.DoesNotContain("PRIVATE-REPLACE-FAULT-CANARY", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanup_failure_does_not_overwrite_the_original_cancellation_status()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        CheckpointEnvelope envelope = CreateEnvelope(input);
        using CancellationTokenSource cancellation = new();
        TestCheckpointFileOperations operations = new();
        operations.FlushOverride = _ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        };
        operations.DeleteOverride = _ => throw new IOException("PRIVATE-CLEANUP-FAULT-CANARY");
        CheckpointStore store = new(operations);

        CheckpointSaveResult result = store.Create(envelope, cancellation.Token);

        Assert.Equal(CheckpointStatusCodes.Cancelled, result.Code);
        Assert.False(File.Exists(envelope.PartialPath));
        string leakedTemp = Assert.Single(Directory.GetFiles(
            Path.GetDirectoryName(envelope.PartialPath)!,
            ".study-report-evaluator-*.checkpoint.xlsx"));
        Assert.DoesNotContain("PRIVATE-CLEANUP-FAULT-CANARY", result.ToString(), StringComparison.Ordinal);
        operations.DeleteOverride = null;
        File.Delete(leakedTemp);
    }

    [Fact]
    public void Exception_after_physical_replace_is_recognized_as_the_valid_new_checkpoint()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        CheckpointEnvelope initial = CreateEnvelope(input);
        TestCheckpointFileOperations operations = new();
        CheckpointStore store = new(operations);
        Assert.True(store.Create(initial, TestContext.Current.CancellationToken).IsSuccess);
        CheckpointEnvelope next = initial with
        {
            SavedAtUtc = initial.SavedAtUtc.AddMinutes(1),
            References = [Reference("Q1", "durable answer", StartedAtUtc.AddSeconds(30))],
        };
        operations.ReplaceOverride = (source, destination) =>
        {
            operations.ReplacePhysical(source, destination);
            throw new IOException("PRIVATE-AFTER-REPLACE-CANARY");
        };

        CheckpointSaveResult result = store.Update(next, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.ToString());
        Assert.Single(Assert.IsType<CheckpointEnvelope>(store.Load(
            next.PartialPath,
            TestContext.Current.CancellationToken).Envelope).References);
        Assert.DoesNotContain("PRIVATE-AFTER-REPLACE-CANARY", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Create_never_overwrites_a_target_created_after_path_reservation()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        CheckpointEnvelope envelope = CreateEnvelope(input);
        byte[] sentinel = "PRIVATE-THIRD-PARTY-TARGET"u8.ToArray();
        File.WriteAllBytes(envelope.PartialPath, sentinel);

        CheckpointSaveResult result = new CheckpointStore().Create(
            envelope,
            TestContext.Current.CancellationToken);

        Assert.Equal(CheckpointStatusCodes.TargetExists, result.Code);
        Assert.Equal(sentinel, File.ReadAllBytes(envelope.PartialPath));
        Assert.DoesNotContain("PRIVATE-THIRD-PARTY-TARGET", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Input_with_a_case_insensitive_checkpoint_sheet_collision_is_rejected_unchanged()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        AddWorksheet(input.Path, "quantification_checkpoint");
        InputSnapshot identity = new InputSnapshotService().Capture(input.Path);
        CheckpointEnvelope envelope = CreateEnvelope(input);

        CheckpointSaveResult result = new CheckpointStore().Create(
            envelope,
            TestContext.Current.CancellationToken);

        Assert.Equal(CheckpointStatusCodes.Invalid, result.Code);
        Assert.False(File.Exists(envelope.PartialPath));
        Assert.True(new InputSnapshotService().Recheck(input.Path, identity).IsMatch);
    }

    [Fact]
    public void Input_identity_drift_and_cancellation_create_no_partial()
    {
        using TemporaryWorkbook changedInput = X01SyntheticWorkbookFactory.Create();
        CheckpointEnvelope changedEnvelope = CreateEnvelope(changedInput);
        File.SetLastWriteTimeUtc(
            changedInput.Path,
            changedEnvelope.Input.LastWriteTimeUtc.UtcDateTime.AddSeconds(2));

        CheckpointSaveResult changed = new CheckpointStore().Create(
            changedEnvelope,
            TestContext.Current.CancellationToken);

        Assert.Equal(CheckpointStatusCodes.InputChanged, changed.Code);
        Assert.False(File.Exists(changedEnvelope.PartialPath));

        using TemporaryWorkbook cancelledInput = X01SyntheticWorkbookFactory.Create();
        CheckpointEnvelope cancelledEnvelope = CreateEnvelope(cancelledInput);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        CheckpointSaveResult cancelled = new CheckpointStore().Create(
            cancelledEnvelope,
            cancellation.Token);

        Assert.Equal(CheckpointStatusCodes.Cancelled, cancelled.Code);
        Assert.False(File.Exists(cancelledEnvelope.PartialPath));
    }

    [Fact]
    public void Invalid_status_and_definition_hash_are_rejected_before_file_creation()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        CheckpointEnvelope envelope = CreateEnvelope(input);
        CheckpointEnvelope unknownStatus = envelope with
        {
            References =
            [
                new CheckpointReference
                {
                    QuestionId = "Q1",
                    StatusCode = "PRIVATE_UNKNOWN_STATUS",
                    GeneratedAtUtc = StartedAtUtc.AddSeconds(10),
                },
            ],
        };
        CheckpointEnvelope wrongDefinitionHash = envelope with
        {
            DefinitionSha256 = new string('B', 64),
        };
        CheckpointStore store = new();

        CheckpointSaveResult statusResult = store.Create(
            unknownStatus,
            TestContext.Current.CancellationToken);
        CheckpointSaveResult hashResult = store.Create(
            wrongDefinitionHash,
            TestContext.Current.CancellationToken);

        Assert.Equal(CheckpointStatusCodes.Invalid, statusResult.Code);
        Assert.Equal(CheckpointStatusCodes.HashMismatch, hashResult.Code);
        Assert.False(File.Exists(envelope.PartialPath));
        Assert.DoesNotContain("PRIVATE_UNKNOWN_STATUS", statusResult.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Update_rejects_mutation_or_removal_of_durable_state()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        CheckpointEnvelope initial = CreateEnvelope(input) with
        {
            References = [Reference("Q1", "first answer", StartedAtUtc.AddSeconds(30))],
        };
        CheckpointStore store = new();
        Assert.True(store.Create(initial, TestContext.Current.CancellationToken).IsSuccess);
        string before = FileHash(initial.PartialPath);
        CheckpointEnvelope mutation = initial with
        {
            SavedAtUtc = initial.SavedAtUtc.AddMinutes(1),
            References = [Reference("Q1", "changed answer", StartedAtUtc.AddSeconds(30))],
        };

        CheckpointSaveResult mutated = store.Update(mutation, TestContext.Current.CancellationToken);
        CheckpointSaveResult removed = store.Update(initial with
        {
            SavedAtUtc = initial.SavedAtUtc.AddMinutes(1),
            References = [],
        }, TestContext.Current.CancellationToken);

        Assert.Equal(CheckpointStatusCodes.Invalid, mutated.Code);
        Assert.Equal(CheckpointStatusCodes.Invalid, removed.Code);
        Assert.Equal(before, FileHash(initial.PartialPath));
    }

    private static CheckpointEnvelope CreateEnvelope(TemporaryWorkbook input)
    {
        QuantificationDefinition definition = U01TestSupport.Definition(
            2,
            2,
            U01TestSupport.Question(
                "Q1",
                "A",
                ["B"],
                true,
                U01TestSupport.Evaluator("E1", "C1")));
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(definition);
        OutputPathReservation reservation = new OutputPathPlanner().Reserve(
            input.Path,
            StartedAtUtc.ToOffset(TimeSpan.FromHours(9)));
        return new CheckpointEnvelope
        {
            InputPath = Path.GetFullPath(input.Path),
            Input = new InputSnapshotService().Capture(input.Path),
            DefinitionCanonicalJson = snapshot.CanonicalJson,
            DefinitionSha256 = snapshot.Sha256,
            NormalModelId = "model-test",
            ReferenceModelId = "model-test",
            Runtime = new CheckpointRuntimeIdentity
            {
                ApplicationIdentity = "StudyReportEvaluator.App/4.0.0",
                CliVersion = "1.0.79",
                CliSha256 = new string('A', 64),
                SdkInformationalVersion = "1.0.11",
            },
            FinalPath = reservation.FinalPath,
            PartialPath = reservation.PartialPath,
            StartedAtUtc = StartedAtUtc,
            SavedAtUtc = StartedAtUtc.AddMinutes(1),
        };
    }

    private static CheckpointReference Reference(
        string questionId,
        string answer,
        DateTimeOffset generatedAtUtc) =>
        new()
        {
            QuestionId = questionId,
            Answer = answer,
            StatusCode = ResultsStatusCodes.Success,
            GeneratedAtUtc = generatedAtUtc,
            AttemptCount = 1,
            TokenUsage = new CheckpointTokenUsage
            {
                IsAvailable = true,
                InputTokens = 10,
                OutputTokens = 20,
            },
        };

    private static CheckpointTokenUsage Usage() =>
        new()
        {
            IsAvailable = true,
            InputTokens = 10,
            OutputTokens = 20,
            ReasoningTokens = 2,
            CacheReadTokens = 3,
            CacheWriteTokens = 4,
        };

    private static void RewriteSingleChunk(string path, Func<string, string> rewrite)
    {
        Mutate(path, worksheet =>
        {
            Row[] payloadRows = worksheet.Descendants<Row>()
                .Where(row => Inline(Cell(row, "A" + row.RowIndex!.Value)) == "PAYLOAD")
                .ToArray();
            Row row = Assert.Single(payloadRows);
            Cell payloadCell = Cell(row, "C5");
            string rewritten = rewrite(Inline(payloadCell));
            Assert.True(rewritten.Length <= 30_000);
            SetInline(payloadCell, rewritten);
            SetInline(
                Cell(worksheet.Descendants<Row>().Single(candidate => candidate.RowIndex?.Value == 3), "C3"),
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rewritten))));
        });
    }

    private static void Mutate(string path, Action<Worksheet> mutation)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(path, true);
        Worksheet worksheet = CheckpointWorksheet(document);
        mutation(worksheet);
        worksheet.Save();
    }

    private static Worksheet CheckpointWorksheet(SpreadsheetDocument document)
    {
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("Synthetic workbook part is missing.");
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("Synthetic workbook root is missing.");
        Sheet sheet = workbook.Descendants<Sheet>().Single(candidate =>
            candidate.Name?.Value == CheckpointStore.CheckpointSheetName);
        return ((WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!)).Worksheet
            ?? throw new InvalidDataException("Synthetic checkpoint worksheet is missing.");
    }

    private static Cell Cell(Row row, string reference) =>
        row.Elements<Cell>().Single(cell => cell.CellReference?.Value == reference);

    private static string Inline(Cell cell) => cell.InlineString?.Text?.Text
        ?? throw new InvalidDataException("Synthetic inline value is missing.");

    private static void SetInline(Cell cell, string value)
    {
        cell.DataType = CellValues.InlineString;
        cell.CellValue = null;
        cell.InlineString = new InlineString(new Text(value));
    }

    private static Cell InlineCell(string reference, string value) =>
        new()
        {
            CellReference = reference,
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new Text(value)),
        };

    private static string WorksheetHash(string workbookPath, string sheetName)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(workbookPath, false);
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("Synthetic workbook part is missing.");
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("Synthetic workbook root is missing.");
        Sheet sheet = workbook.Descendants<Sheet>().Single(candidate =>
            candidate.Name?.Value == sheetName);
        OpenXmlPart part = workbookPart.GetPartById(sheet.Id!.Value!);
        using Stream stream = part.GetStream(FileMode.Open, FileAccess.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string[] SheetNames(string workbookPath)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(workbookPath, false);
        Workbook workbook = document.WorkbookPart?.Workbook
            ?? throw new InvalidDataException("Synthetic workbook root is missing.");
        return workbook.Descendants<Sheet>()
            .Select(sheet => sheet.Name?.Value
                ?? throw new InvalidDataException("Synthetic worksheet name is missing."))
            .ToArray();
    }

    private static string FileHash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void AddWorksheet(string path, string name)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(path, true);
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("Synthetic workbook part is missing.");
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("Synthetic workbook root is missing.");
        WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        worksheetPart.Worksheet = new Worksheet(new SheetData());
        Sheets sheets = workbook.GetFirstChild<Sheets>()
            ?? throw new InvalidDataException("Synthetic sheets are missing.");
        uint nextId = sheets.Elements<Sheet>().Max(sheet => sheet.SheetId!.Value) + 1;
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = nextId,
            Name = name,
        });
        workbook.Save();
    }

    private sealed class TestCheckpointFileOperations : ICheckpointFileOperations
    {
        private readonly PhysicalCheckpointFileOperations physical = new();

        public Action<string, string>? ReplaceOverride { get; set; }

        public Action<string>? FlushOverride { get; set; }

        public Action<string>? DeleteOverride { get; set; }

        public bool Exists(string path) => physical.Exists(path);

        public void FlushToDisk(string path)
        {
            if (FlushOverride is not null)
            {
                FlushOverride(path);
                return;
            }

            physical.FlushToDisk(path);
        }

        public void MoveNoOverwrite(string sourcePath, string destinationPath) =>
            physical.MoveNoOverwrite(sourcePath, destinationPath);

        public void Replace(string sourcePath, string destinationPath)
        {
            if (ReplaceOverride is not null)
            {
                ReplaceOverride(sourcePath, destinationPath);
                return;
            }

            physical.Replace(sourcePath, destinationPath);
        }

        public void Delete(string path)
        {
            if (DeleteOverride is not null)
            {
                DeleteOverride(path);
                return;
            }

            physical.Delete(path);
        }

        public void ReplacePhysical(string sourcePath, string destinationPath) =>
            physical.Replace(sourcePath, destinationPath);
    }
}
