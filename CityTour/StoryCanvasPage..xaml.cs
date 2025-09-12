namespace CityTour;

public partial class StoryCanvasPage : ContentPage
{
    private readonly string _placeId;

    public StoryCanvasPage(string placeId, string buildingName)
    {
        InitializeComponent();
        _placeId = placeId;          // not used yet, but we’ll need it when we save
        BuildingNameLabel.Text = buildingName;
        StoryEditor.Text = string.Empty; // start empty
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
