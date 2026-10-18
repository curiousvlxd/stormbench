namespace StormBench.Scada.Hmi;

public static class HmiServices
{
    extension(IServiceCollection services)
    {
        /// <summary>The operator display and the Live view: Blazor Server with the read model behind both.</summary>
        public IServiceCollection AddHmi()
        {
            services.AddSingleton<HmiFeed>();
            services.AddRazorComponents().AddInteractiveServerComponents();
            return services;
        }
    }
}
