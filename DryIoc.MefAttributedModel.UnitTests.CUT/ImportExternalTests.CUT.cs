﻿using System;
using System.ComponentModel.Composition;

namespace DryIoc.MefAttributedModel.UnitTests.CUT
{
    [Export, TransientReuse]
    public class NativeUser
    {
        public IForeignTool Tool { get; set; }

        public NativeUser([ImportExternal(typeof(ForeignTool), contractType: typeof(ForeignTool)), TransientReuse] IForeignTool tool)
        {
            Tool = tool;
        }
    }

    public interface IForeignTool
    {
    }

    public class ForeignTool : IForeignTool
    {
    }

    [Export]
    public class HomeUser
    {
        public ExternalTool Tool { get; set; }

        public HomeUser([ImportExternal(withConstructor: new[] { typeof(string) })] Func<string, ExternalTool> getTool)
        {
            Tool = getTool("blah");
        }
    }

    public class ExternalTool
    {
        public string Message { get; set; }

        public ExternalTool()
        {
        }

        public ExternalTool(string message)
        {
            Message = message;
        }
    }

    public enum MineMeta
    {
        Red,
        Green
    };

    [ExportAll]
    public class MyCode
    {
        public ExternalTool Tool { get; set; }
        public MineMeta ToolMeta { get; set; }

        public MyCode(
            [ImportExternal(Metadata = MineMeta.Green, WithConstructor = new Type[0])] 
            Meta<Lazy<ExternalTool>, MineMeta> tool)
        {
            Tool = tool.Value.Value;
            ToolMeta = tool.Metadata;
        }
    }

    [ExportAll]
    public class ServiceWithFieldAndProperty
    {
        [ImportExternal(typeof(AnotherService), contractKey: "blah")]
        public IService Field;

        [ImportExternal(ImplementationType = typeof(AnotherService), ContractKey = "blah")]
        public IService Property { get; set; }
    }

    [Export]
    public class OneDependsOnExternalTool
    {
        public readonly ExternalTool Tool;

        public OneDependsOnExternalTool([ImportExternal(withConstructor: new Type[0], contractKey: 13)]ExternalTool tool)
        {
            Tool = tool;
        }
    }

    [Export]
    public class OtherDependsOnExternalTool
    {
        public readonly ExternalTool Tool;

        public OtherDependsOnExternalTool([ImportWithKey(13)]ExternalTool tool)
        {
            Tool = tool;
        }
    }

    [ExportAll]
    public class WithUnregisteredExternalEdependency
    {
        public ExternalTool Tool { get; set; }

        public WithUnregisteredExternalEdependency([SomeOther]ExternalTool tool)
        {
            Tool = tool;
        }
    }

    public class SomeOtherAttribute : Attribute
    {
    }
}