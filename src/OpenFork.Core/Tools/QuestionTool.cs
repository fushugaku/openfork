using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenFork.Core.Tools;

public class QuestionTool : ITool
{
    public string Name => "question";

    public string Description => PromptLoader.Load("question",
        "Ask the user questions to gather preferences, clarify requirements, or get decisions on implementation choices.");

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            questions = new
            {
                type = "array",
                description = "Array of questions to ask the user",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        question = new
                        {
                            type = "string",
                            description = "The question text to display to the user"
                        },
                        options = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            description = "List of options for the user to choose from. Put recommended option first with '(Recommended)' suffix."
                        },
                        multiple = new
                        {
                            type = "boolean",
                            description = "Allow selecting multiple options (default: false)"
                        }
                    },
                    required = new[] { "question", "options" }
                }
            }
        },
        required = new[] { "questions" }
    };

    public async Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
    {
        try
        {
            var args = JsonSerializer.Deserialize<QuestionArgs>(arguments, JsonHelper.Options);
            
            if (args?.Questions == null || args.Questions.Count == 0)
                return new ToolResult(false, "At least one question is required");

            if (context.AskUserAsync == null)
                return new ToolResult(false, "User interaction not available in this context");

            var questionRequest = new QuestionRequest
            {
                Questions = args.Questions.Select(q => new Question
                {
                    Text = q.QuestionText ?? "",
                    Options = q.Options ?? new List<string>(),
                    AllowMultiple = q.Multiple ?? false,
                    AllowCustom = true
                }).ToList()
            };

            var answers = await context.AskUserAsync(questionRequest);

            if (answers.Count == 0)
                return new ToolResult(true, "User did not provide answers.");

            var formatted = args.Questions.Select((q, i) =>
            {
                var answer = i < answers.Count ? answers[i] : null;
                var answerText = answer?.Answers.Count > 0 
                    ? string.Join(", ", answer.Answers) 
                    : "Unanswered";
                return $"\"{q.QuestionText}\" = \"{answerText}\"";
            });

            return new ToolResult(true, 
                $"User has answered your questions: {string.Join(", ", formatted)}. You can now continue with the user's answers in mind.");
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Error asking questions: {ex.Message}");
        }
    }

    private record QuestionArgs(
        [property: JsonPropertyName("questions")] List<QuestionItem>? Questions
    );

    private record QuestionItem(
        [property: JsonPropertyName("question")] string? QuestionText,
        [property: JsonPropertyName("options")] List<string>? Options,
        [property: JsonPropertyName("multiple")] bool? Multiple
    );
}
