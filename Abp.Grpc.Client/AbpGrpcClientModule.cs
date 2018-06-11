﻿using Abp.Grpc.Client.Configuration;
using Abp.Grpc.Client.Infrastructure.Consul;
using Abp.Grpc.Client.Infrastructure.GrpcChannel;
using Abp.Modules;
using Polly;
using System.Linq;

namespace Abp.Grpc.Client
{
    [DependsOn(typeof(AbpKernelModule))]
    public class AbpGrpcClientModule : AbpModule
    {
        private IGrpcClientConfiguration _grpcClientConfiguration;

        public override void PreInitialize()
        {
            IocManager.Register<IConsulClientFactory, ConsulClientFactory>();
            IocManager.Register<IGrpcClientConfiguration, GrpcClientConfiguration>();
            IocManager.Register<IGrpcChannelFactory, GrpcChannelFactory>();
        }

        public override void Initialize()
        {
            IocManager.RegisterAssemblyByConvention(typeof(AbpGrpcClientModule).Assembly);
        }

        public override void PostInitialize()
        {
            _grpcClientConfiguration = IocManager.Resolve<IGrpcClientConfiguration>();
            ScanAllAvailableGrpcServices(_grpcClientConfiguration);
        }

        public override void Shutdown()
        {
            foreach (var channels in _grpcClientConfiguration.GrpcServers.Values)
            {
                foreach (var channel in channels)
                {
                    channel.ShutdownAsync().Wait();
                }
            }
        }

        private void ScanAllAvailableGrpcServices(IGrpcClientConfiguration config)
        {
            var consulClient = IocManager.Resolve<IConsulClientFactory>().Get(config.ConsulRegistryConfiguration);
            var grpcChannelFactory = IocManager.Resolve<IGrpcChannelFactory>();

            var policy = Policy.TimeoutAsync(5, (context, span, arg3) => throw new AbpInitializationException("无法连接到 Consul 集群."));

            policy.ExecuteAsync(async () =>
            {
                var services = await consulClient.Catalog.Services();
                foreach (var service in services.Response)
                {
                    var serviceInfo = await consulClient.Catalog.Service(service.Key);
                    var grpcServerInfo = serviceInfo.Response.SkipWhile(z => !z.ServiceTags.Contains("GRpc"));

                    foreach (var info in grpcServerInfo)
                    {
                        if (_grpcClientConfiguration.GrpcServers.ContainsKey(info.ServiceName))
                        {
                            _grpcClientConfiguration.GrpcServers[info.ServiceName].Add(grpcChannelFactory.Get(info.ServiceAddress, info.ServicePort));
                        }
                    }
                }
            });
        }
    }
}