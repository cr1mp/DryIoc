﻿/*
The MIT License (MIT)

Copyright (c) 2016 Maksim Volkau

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace DryIoc.Dnx.DependencyInjection
{
    /// <summary>DI adapter for DryIoc. Basically it is a DryIoc implementation of <see cref="IServiceProvider"/>.</summary>
    public static class DryIocDnxDependencyInjection
    {
        /// <summary>Creates new container from the <paramref name="container"/> adapted to be used
        /// with AspNetCore Dependency Injection:
        /// - First configures container with DI conventions.
        /// - Then registers DryIoc implementations of <see cref="IServiceProvider"/> and <see cref="IServiceScopeFactory"/>.
        /// </summary>
        /// <param name="container">Source container to adapt.</param>
        /// <param name="descriptors">(optional) Specify service descriptors or use <see cref="Populate"/> later.</param>
        /// <param name="registerDescriptor">(optional) Custom registration action, should return true to skip normal registration.</param>
        /// <returns>New container adapted to AspNetCore DI conventions.</returns>
        /// <example>
        /// <code><![CDATA[
        ///     container = new Container().WithDependencyInjectionAdapter(services);
        ///     serviceProvider = container.Resolve<IServiceProvider>();
        ///     
        ///     // To register your service to be reused in Request use Reuse.InCurrentScope
        ///     container.Register<IMyService, MyService>(Reuse.InCurrentScope)
        /// ]]></code>
        /// </example>
        public static IContainer WithDependencyInjectionAdapter(this IContainer container,
            IEnumerable<ServiceDescriptor> descriptors = null,
            Func<IRegistrator, ServiceDescriptor, bool> registerDescriptor = null)
        {
            if (container.ScopeContext != null)
                throw new ArgumentException("Adapted container uses ambient scope context which is not supported by AspNetCore DI.");

            var adapter = container.With(rules => rules
                .With(FactoryMethod.ConstructorWithResolvableArguments)
                .WithFactorySelector(Rules.SelectLastRegisteredFactory())
                .WithTrackingDisposableTransients()
                .WithImplicitRootOpenScope());

            adapter.Register<IServiceProvider, DryIocServiceProvider>(Reuse.InCurrentScope);

            // Scope factory should be scoped itself to enable nested scopes creation
            adapter.Register<IServiceScopeFactory, DryIocServiceScopeFactory>(Reuse.InCurrentScope);

            // Register asp net abstractions specified by descriptors in container 
            if (descriptors != null)
                adapter.Populate(descriptors, registerDescriptor);

            return adapter;
        }

        /// <summary>Registers service descriptors into container. May be called multiple times with different service collections.</summary>
        /// <param name="container">The container.</param>
        /// <param name="descriptors">The service descriptors.</param>
        /// <param name="registerDescriptor">(optional) Custom registration action, should return true to skip normal registration.</param>
        /// <example>
        /// <code><![CDATA[
        ///     // example of normal descriptor registration together with factory method registration for SomeService.
        ///     container.Populate(services, (r, service) => {
        ///         if (service.ServiceType == typeof(SomeService)) {
        ///             r.Register<SomeService>(Made.Of(() => CreateCustomService()), Reuse.Singleton);
        ///             return true;
        ///         };
        ///         return false; // fallback to normal registrations for the rest of the descriptors.
        ///     });
        /// ]]></code>
        /// </example>
        public static void Populate(this IContainer container, IEnumerable<ServiceDescriptor> descriptors,
            Func<IRegistrator, ServiceDescriptor, bool> registerDescriptor = null)
        {
            foreach (var descriptor in descriptors)
            {
                if (registerDescriptor == null || !registerDescriptor(container, descriptor))
                    container.RegisterDescriptor(descriptor);
            }
        }

        /// <summary>Uses passed descriptor to register service in container: 
        /// maps DI Lifetime to DryIoc Reuse,
        /// and DI registration type to corresponding DryIoc Register, RegisterDelegate or RegisterInstance.</summary>
        /// <param name="container">The container.</param>
        /// <param name="descriptor">Service descriptor.</param>
        public static void RegisterDescriptor(this IContainer container, ServiceDescriptor descriptor)
        {
            var reuse = ConvertLifetimeToReuse(descriptor.Lifetime);

            if (descriptor.ImplementationType != null)
            {
                container.Register(descriptor.ServiceType, descriptor.ImplementationType, reuse);
            }
            else if (descriptor.ImplementationFactory != null)
            {
                container.RegisterDelegate(descriptor.ServiceType,
                    r => descriptor.ImplementationFactory(r.Resolve<IServiceProvider>()), 
                    reuse);
            }
            else
            {
                container.RegisterInstance(descriptor.ServiceType, descriptor.ImplementationInstance, reuse);
            }
        }

        private static IReuse ConvertLifetimeToReuse(ServiceLifetime lifetime)
        {
            switch (lifetime)
            {
                case ServiceLifetime.Singleton:
                    return Reuse.Singleton;
                case ServiceLifetime.Scoped:
                    return Reuse.InCurrentScope;
                case ServiceLifetime.Transient:
                    return Reuse.Transient;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Not supported lifetime");
            }
        }
    }

    /// <summary>Delegates service resolution to wrapped DryIoc scoped container.
    /// When disposed, disposes the scoped container.</summary>
    public sealed class DryIocServiceProvider : IServiceProvider, IDisposable
    {
        private readonly IContainer _scopedContainer;

        /// <summary>Uses passed container for scoped resolutions.</summary> 
        /// <param name="scopedContainer">subj.</param>
        public DryIocServiceProvider(IContainer scopedContainer)
        {
            _scopedContainer = scopedContainer;
        }

        /// <summary>Delegates resolution to scoped container. 
        /// Uses <see cref="IfUnresolved.ReturnDefault"/> policy to return default value in case of resolution error.</summary>
        /// <param name="serviceType">Service type to resolve.</param>
        /// <returns>Resolved service object.</returns>
        public object GetService(Type serviceType)
        {
            return _scopedContainer.Resolve(serviceType, ifUnresolvedReturnDefault: true);
        }

        /// <summary>Disposes scoped container and scope.</summary>
        public void Dispose()
        {
            _scopedContainer.Dispose();
        }
    }

    /// <summary>The goal of the factory is create / open new scope.
    /// The factory itself is scoped (not a singleton). 
    /// That means you need to resolve factory from outer scope to create nested scope.</summary>
    public sealed class DryIocServiceScopeFactory : IServiceScopeFactory
    {
        private readonly IContainer _scopedContainer;

        /// <summary>Stores passed scoped container to open nested scope.</summary>
        /// <param name="scopedContainer">Scoped container to be used to create nested scope.</param>
        public DryIocServiceScopeFactory(IContainer scopedContainer)
        {
            _scopedContainer = scopedContainer;
        }

        /// <summary>Opens scope and wraps it into DI <see cref="IServiceScope"/> interface.</summary>
        /// <returns>DI wrapper of opened scope.</returns>
        public IServiceScope CreateScope()
        {
            var scope = _scopedContainer.OpenScope();
            return new DryIocServiceScope(scope.Resolve<IServiceProvider>());
        }

        private sealed class DryIocServiceScope : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; }

            public DryIocServiceScope(IServiceProvider serviceProvider)
            {
                ServiceProvider = serviceProvider;
            }

            public void Dispose() => (ServiceProvider as IDisposable)?.Dispose();
        }
    }
}