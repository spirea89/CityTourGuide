namespace CityTour
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute(nameof(Views.DetailPage), typeof(Views.DetailPage));
        }
    }
}
