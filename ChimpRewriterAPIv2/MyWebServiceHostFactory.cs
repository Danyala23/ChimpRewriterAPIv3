using System;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Web;
using ChimpRewriterAPIv3.API;

namespace ChimpRewriterAPIv3
{
    public class MyServiceHost : WebServiceHost
    {
        public MyServiceHost()
        {
        }

        public MyServiceHost(object singletonInstance, params Uri[] baseAddresses)
            : base(singletonInstance, baseAddresses)
        {
        }

        public MyServiceHost(Type serviceType, params Uri[] baseAddresses)
            : base(serviceType, baseAddresses)
        {
        }

        protected override void ApplyConfiguration()
        {
            base.ApplyConfiguration();
            foreach (var endpoint in this.Description.Endpoints)
            {
                var binding = endpoint.Binding;
                APIUsers.TestString += binding.GetType().ToString();
                if (binding is WebHttpBinding)
                {
                    var web = binding as WebHttpBinding;
                    web.MaxBufferSize = 2000000;
                    web.MaxBufferPoolSize = 2000000;
                    web.MaxReceivedMessageSize = 2000000;
                }
                var myReaderQuotas = new System.Xml.XmlDictionaryReaderQuotas();
                myReaderQuotas.MaxStringContentLength = 2000000;
                myReaderQuotas.MaxArrayLength = 2000000;
                myReaderQuotas.MaxDepth = 32;
                binding.GetType().GetProperty("ReaderQuotas").SetValue(binding, myReaderQuotas, null);
            }
        }
    }


    class MyWebServiceHostFactory : WebServiceHostFactory
    {
        protected override ServiceHost CreateServiceHost(Type serviceType, Uri[] baseAddresses)
        {
            return new MyServiceHost(serviceType, baseAddresses);
        }
    }
}