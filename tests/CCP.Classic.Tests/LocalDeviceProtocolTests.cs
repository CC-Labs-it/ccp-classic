using CCP.Classic.Core.LocalDevices;
using Xunit;

namespace CCP.Classic.Tests;

public sealed class LocalDeviceProtocolTests
{
    [Fact]
    public void IntifaceSpecFourMapsAdvertisedFeaturesWithoutInventingActuators()
    {
        var devices = LocalDeviceProtocol.IntifaceDevices(LocalDeviceProtocol.Parse("""
            {"Devices":{"2":{"DeviceName":"Two motors","DeviceIndex":2,"DeviceFeatures":{
              "0":{"FeatureIndex":0,"Output":{"Vibrate":{"Value":[0,20]}}},
              "1":{"FeatureIndex":1,"Output":{"Rotate":{"Value":[-100,100]}}},
              "2":{"FeatureIndex":2,"Output":{"Position":{"Value":[0,100]},"HwPositionWithDuration":{"Value":[0,100],"Duration":[50,500]}}},
              "3":{"FeatureIndex":3,"Input":{"Battery":{"Value":[0,100]}}},
              "4":{"FeatureIndex":4,"Output":{"Temperature":{"Value":[0,100]}}}
            }}}}
            """));
        var device = Assert.Single(devices); Assert.Equal("2", device.Id); Assert.Equal(3, device.Actuators.Count);
        Assert.Equal(LocalActuatorKind.Vibrate, device.Actuators[0].Kind);
        Assert.Equal(LocalActuatorKind.Rotate, device.Actuators[1].Kind);
        Assert.Equal("HwPositionWithDuration", device.Actuators[2].Command);
        Assert.Equal(50, device.Actuators[2].MinimumDuration);
        Assert.Equal(10, LocalDeviceProtocol.NativeLevel(device.Actuators[0], .5));
        Assert.Equal(100, LocalDeviceProtocol.NativeLevel(device.Actuators[1], 5));
        Assert.Equal(0, LocalDeviceProtocol.NativeLevel(device.Actuators[0], double.NaN));
    }

    [Fact]
    public void LovenseEscapedToyMapsRetainNativeFunctionResolutionAndOnlineStatus()
    {
        const string json = """
            {"code":200,"data":{"toys":"{\"a\":{\"id\":\"a\",\"name\":\"edge\",\"status\":\"1\",\"shortFunctionNames\":[\"v1\",\"v2\"]},\"b\":{\"id\":\"b\",\"name\":\"solace\",\"status\":1},\"c\":{\"id\":\"c\",\"name\":\"lush\",\"status\":0}}"}}
            """;
        var devices = LocalDeviceProtocol.LovenseDevices(LocalDeviceProtocol.Parse(json));
        Assert.Equal(2, devices.Count);
        Assert.Equal("Vibrate1:10,Vibrate2:10", LocalDeviceProtocol.LovenseAction(devices[0], .5));
        Assert.Equal(LocalActuatorKind.Thrust, devices[1].Actuators[0].Kind);
        Assert.Equal(LocalActuatorKind.Depth, devices[1].Actuators[1].Kind);
        Assert.Equal("Thrusting:10,Depth:2", LocalDeviceProtocol.LovenseAction(devices[1], .5));
        Assert.DoesNotContain(devices[1].Actuators, a => a.Kind == LocalActuatorKind.Vibrate);
    }

    [Fact]
    public void InvalidResponsesCannotEraseARegistryAsAValidEmptyList()
    {
        Assert.Throws<IOException>(() => LocalDeviceProtocol.LovenseDevices(LocalDeviceProtocol.Parse("{\"code\":500}")));
        Assert.Throws<IOException>(() => LocalDeviceProtocol.IntifaceDevices(LocalDeviceProtocol.Parse("{}")));
        Assert.Throws<IOException>(() => LocalDeviceProtocol.Parse(new string(' ', 65537)));
        Assert.Empty(LocalDeviceProtocol.LovenseDevices(LocalDeviceProtocol.Parse("{\"code\":200,\"data\":{\"toys\":{}}}")));
        Assert.Empty(LocalDeviceProtocol.IntifaceDevices(LocalDeviceProtocol.Parse("{\"Devices\":{}}")));
    }

    [Fact]
    public void DuplicateIndicesUnsupportedFunctionsAndUnsafeRangesAreIgnored()
    {
        var body = LocalDeviceProtocol.Parse("""
            {"Devices":{"0":{"DeviceName":"Invalid","DeviceIndex":1,"DeviceFeatures":{}},
              "1":{"DeviceName":"Ranges","DeviceIndex":1,"DeviceFeatures":{
                "0":{"FeatureIndex":0,"Output":{"Vibrate":{"Value":[1,20]}}},
                "1":{"FeatureIndex":1,"Output":{"Vibrate":{"Value":[0,1000000000]}}}
              }}}}
            """);
        Assert.Empty(LocalDeviceProtocol.IntifaceDevices(body));
        var lovense = LocalDeviceProtocol.LovenseDevices(LocalDeviceProtocol.Parse("""
            {"data":{"toys":{"a":{"name":"unknown","shortFunctionNames":["v1","v1","invalid","v999"]}}}}
            """));
        Assert.Single(Assert.Single(lovense).Actuators);
    }
}
