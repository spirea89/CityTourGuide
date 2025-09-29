namespace CityTour.Models
{
    public sealed class StoryGenerationResult
    {
        public StoryGenerationResult(string story, string prompt)
        {
            Story = story;
            Prompt = prompt;
        }

        public string Story { get; }

        public string Prompt { get; }
    }
}
