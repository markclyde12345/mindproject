    namespace project
    {
        public partial class App : Application
        {
            public App()
            {
                InitializeComponent();

                FirebaseService.Initialize();
            }

            protected override Window CreateWindow(IActivationState? activationState)
            {
                return new Window(new AppShell());
            }
        }
    }