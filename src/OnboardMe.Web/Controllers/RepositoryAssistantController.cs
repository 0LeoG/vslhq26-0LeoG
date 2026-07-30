using Microsoft.AspNetCore.Mvc;
using OnboardMe.Web.Services.RepoIngestion;

namespace OnboardMe.Web.Controllers;

[ApiController]
[Route("repos/{owner}/{repository}")]
public sealed class RepositoryAssistantController(
    IAzureOpenAiEmbeddingService embeddingService,
    IRepositoryEmbeddingStore embeddingStore,
    IRepositoryIngestionService repositoryIngestionService,
    IAzureOpenAiChatService chatService) : ControllerBase
{
    [HttpPost("embeddings/rerun")]
    public async Task<IActionResult> RerunEmbeddings(
        string owner,
        string repository,
        CancellationToken cancellationToken)
    {
        try
        {
            var embeddedChunks = await repositoryIngestionService.RegenerateEmbeddingsAsync(owner, repository, cancellationToken);
            return Ok(new { owner, repository, embeddedChunks });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search(
        string owner,
        string repository,
        [FromBody] SearchRequest body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Query))
        {
            return BadRequest(new { message = "Query must not be empty." });
        }

        var topK = body.TopK is > 0 ? body.TopK.Value : 5;

        // Embed the query as a synthetic chunk so we can reuse the same embedding pipeline.
        var queryChunk = new RepositoryContentChunk
        {
            ChunkId = "query:0",
            SourcePath = "__query__",
            SourceSha = string.Empty,
            ChunkIndex = 0,
            Strategy = "query",
            StartLine = 0,
            EndLine = 0,
            Content = body.Query
        };

        IReadOnlyList<RepositoryChunkEmbeddingRecord> queryEmbeddings;
        try
        {
            queryEmbeddings = await embeddingService.GenerateEmbeddingsAsync(owner, repository, [queryChunk], cancellationToken);
        }
        catch (Exception ex)
        {
            return Problem(
                title: "Embedding generation failed.",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }

        var queryEmbedding = queryEmbeddings[0].Embedding;
        var results = await embeddingStore.SearchByEmbeddingAsync(owner, repository, queryEmbedding, topK, cancellationToken);

        return Ok(new
        {
            owner,
            repository,
            query = body.Query,
            results = results.Select(r => new
            {
                chunkId = r.Chunk.ChunkId,
                sourcePath = r.Chunk.SourcePath,
                startLine = r.Chunk.StartLine,
                endLine = r.Chunk.EndLine,
                score = r.Score,
                content = r.Chunk.Content
            })
        });
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat(
        string owner,
        string repository,
        [FromBody] ChatRequest body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Question))
        {
            return BadRequest(new { message = "Question must not be empty." });
        }

        var topK = body.TopK is > 0 ? body.TopK.Value : 5;

        // Embed the question, retrieve context chunks, then request a grounded chat answer.
        var queryChunk = new RepositoryContentChunk
        {
            ChunkId = "query:0",
            SourcePath = "__query__",
            SourceSha = string.Empty,
            ChunkIndex = 0,
            Strategy = "query",
            StartLine = 0,
            EndLine = 0,
            Content = body.Question
        };

        IReadOnlyList<RepositoryChunkEmbeddingRecord> queryEmbeddings;
        try
        {
            queryEmbeddings = await embeddingService.GenerateEmbeddingsAsync(owner, repository, [queryChunk], cancellationToken);
        }
        catch (Exception ex)
        {
            return Problem(
                title: "Embedding generation failed.",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }

        var queryEmbedding = queryEmbeddings[0].Embedding;
        var contextChunks = await embeddingStore.SearchByEmbeddingAsync(owner, repository, queryEmbedding, topK, cancellationToken);

        ChatAnswer chatAnswer;
        try
        {
            chatAnswer = await chatService.AnswerAsync(owner, repository, body.Question, contextChunks, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            return Problem(
                title: "Chat completion failed.",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Ok(new
        {
            owner,
            repository,
            question = body.Question,
            answer = chatAnswer.Answer,
            citations = chatAnswer.Citations.Select(c => new
            {
                path = c.Path,
                startLine = c.StartLine,
                endLine = c.EndLine
            })
        });
    }
}
