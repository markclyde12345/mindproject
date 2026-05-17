using Microsoft.Maui.Controls;

namespace project;

public partial class GreatValidationPage : ContentPage
{
    public GreatValidationPage()
    {
        InitializeComponent(); // wires up all x:Name controls from XAML
    }

    private async void OnSaveGratitudeClicked(object sender, EventArgs e)
    {
        // Read what the user typed
        var gratitudeText = GratitudeEntry.Text;

        if (string.IsNullOrWhiteSpace(gratitudeText))
        {
            await DisplayAlert("Oops", "Please enter something you're grateful for!", "OK");
            return;
        }

        // Here you can save the text to a database, file, or app state
        // Example: just show a confirmation
        await DisplayAlert("Saved", "Your gratitude has been recorded!", "OK");

        // Clear entry if needed
        GratitudeEntry.Text = string.Empty;
    }
}