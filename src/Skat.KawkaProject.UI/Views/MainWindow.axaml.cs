using System;
using System.Net;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Confluent.Kafka;
using Skat.KawkaProject.UI.ViewModels;

namespace Skat.KawkaProject.UI.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public void SendMessageButton_OnClick(object sender, RoutedEventArgs e)
        {
            var vm = (MainWindowViewModel) this.DataContext;
            try
            {
                var s = $"{vm.Message} - {vm.KafkaServer} - {vm.TopicName}";

                var t = s.Length;

                var config = new ProducerConfig()
                {
                    BootstrapServers = vm.KafkaServer,
                    ClientId = Dns.GetHostName()
                };

                using (var producer = new ProducerBuilder<Null, string>(config).Build())
                {
                    producer.ProduceAsync(vm.TopicName, new Message<Null, string>() {Value = vm.Message}).Wait();
                }

                vm.ReturnMessage = "Mensagem enviada";

            }
            catch (Exception exception)
            {
                vm.ReturnMessage = exception.Message;
            }
        }
    }
}