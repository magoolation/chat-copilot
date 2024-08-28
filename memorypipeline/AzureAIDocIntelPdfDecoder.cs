// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.DataFormats;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.Pipeline;

namespace CopilotChat.MemoryPipeline;

//[Experimental("KMEXP00")]
public sealed class AzureAIDocIntelPdfDecoder : IContentDecoder
{
    private readonly DocumentAnalysisClient _recognizerClient;
    private readonly ILogger<AzureAIDocIntelPdfDecoder> _log;

    public AzureAIDocIntelPdfDecoder(
        AzureAIDocIntelConfig config,
        ILoggerFactory? loggerFactory = null)
    {
        this._log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<AzureAIDocIntelPdfDecoder>();

        switch (config.Auth)
        {
            case AzureAIDocIntelConfig.AuthTypes.AzureIdentity:
                this._recognizerClient = new DocumentAnalysisClient(new Uri(config.Endpoint), new DefaultAzureCredential());
                break;

            case AzureAIDocIntelConfig.AuthTypes.APIKey:
                if (string.IsNullOrEmpty(config.APIKey))
                {
                    this._log.LogCritical("Azure AI Document Intelligence API key is empty");
                    throw new ConfigurationException("Azure AI Document Intelligence API key is empty");
                }

                this._recognizerClient = new DocumentAnalysisClient(new Uri(config.Endpoint), new AzureKeyCredential(config.APIKey));
                break;

            default:
                this._log.LogCritical("Azure AI Document Intelligence authentication type '{0}' undefined or not supported", config.Auth);
                throw new ConfigurationException($"Azure AI Document Intelligence authentication type '{config.Auth}' undefined or not supported");
        }
    }

    /// <inheritdoc />
    public bool SupportsMimeType(string mimeType)
    {
        return mimeType != null && mimeType.StartsWith(MimeTypes.Pdf, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public Task<FileContent> DecodeAsync(string filename, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(filename);
        return this.DecodeAsync(stream, cancellationToken);
    }

    /// <inheritdoc />
    public Task<FileContent> DecodeAsync(BinaryData data, CancellationToken cancellationToken = default)
    {
        using var stream = data.ToStream();
        return this.DecodeAsync(stream, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FileContent> DecodeAsync(Stream data, CancellationToken cancellationToken = default)
    {
        this._log.LogDebug("Extracting text from PDF file");

        var result = new FileContent(MimeTypes.PlainText);
        var operation = await this._recognizerClient.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-read", data, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Wait for the result
        Response<AnalyzeResult> operationResponse = await operation.WaitForCompletionAsync(cancellationToken).ConfigureAwait(false);

        int pageNumber = 1;
        foreach (var page in operationResponse.Value.Pages)
        {
            // Note: no trimming, use original spacing
            string pageContent = string.Join("\r\n", page.Lines.Select(c => c.Content));
            result.Sections.Add(new FileSection(pageNumber++, pageContent, false));
        }

        return result;
    }
}
