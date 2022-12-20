using Barotrauma.Networking;

using Microsoft.Xna.Framework;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace Barotrauma.Items.Components
{

  class RouterComponent : ItemComponent {

    public enum RouterLayout
    {
      Single,
      Double,
      Triple
    }

    private readonly Dictionary<string, List<string>> conGroupings = new Dictionary<string, List<string>>();

    private const int MinChannel = 0;
    private readonly int MaxChannel;

    private readonly int signalsPerChannel;
    private readonly int channelTotal;
    private readonly RouterLayout layout;

    private int currentChannel;

    [InGameEditable(MinValueInt = 0, MaxValueInt = 5), Serialize(0, IsPropertySaveable.Yes, description: "The current channel signals will be routed to (clamped).", alwaysUseInstanceValues: true)]
    public int CurrentChannel
    {
      get { return currentChannel; }
      set
      {
        currentChannel = MathHelper.Clamp(value, MinChannel, MaxChannel);
      }
    }

    [Editable(ReadOnly = true), Serialize(null, IsPropertySaveable.No, description: "The number of parallel running connections in each channel.")]
    public int SignalsPerChannel { get { return signalsPerChannel; } set {} }
    [Editable(ReadOnly = true), Serialize(null, IsPropertySaveable.No, description: "The total number of output channels.")]
    public int ChannelTotal { get { return channelTotal; } set {} }
    public RouterLayout Layout { get { return layout; } }

    public RouterComponent(Item item, ContentXElement element) : base(item, element)
    {

      string str = element.GetAttributeStringUnrestricted("layout", "Double");
      // layout validation
      if (!Enum.TryParse(str, true, out RouterLayout rLayout))
      {
        DebugConsole.ThrowError($"Invalid layout type \"{str}\" ({item.Prefab.ContentFile.Path})");
      }
      else
      {
        // setting constant values
        switch (rLayout)
        {
          case RouterLayout.Single:
            signalsPerChannel = 1;
            channelTotal = 6;
            break;
          case RouterLayout.Double:
            signalsPerChannel = 2;
            channelTotal = 3;
            break;
          case RouterLayout.Triple:
            signalsPerChannel = 3;
            channelTotal = 2;
            break;
        }

        layout = rLayout;
        MaxChannel = ChannelTotal - 1;

        // populate groupings dict
        Dictionary<string, List<string>> dict = new Dictionary<string, List<string>>();
        for (int s = 0; s < SignalsPerChannel; s++)
        {
          // inputs "signal_in[#sig]
          // outputs if multiple per channel "signal_out[#chan]_[#sig]
          // outputs else "signal_out[#sig]
          List<string> channels = new List<string>();
          string key = $"signal_in{(SignalsPerChannel == 1 ? "" : s)}";
          for (int c = 0; c < ChannelTotal; c++)
          {
            channels.Add($"signal_out{c}{(SignalsPerChannel == 1 ? "" : $"_{s}")}");
          }

          conGroupings.Add(key, channels);
        }
      }

      // set component active
      IsActive = true;
    }

    public override void OnItemLoaded()
    {
      base.OnItemLoaded();
      var connections = Item.Connections;
      // validate connections against own dict, all members of a specific grouping being absent is considered valid
      if (connections != null)
      {
        foreach (KeyValuePair<string, List<string>> grouping in conGroupings)
        {
          if (connections.Any(con => con.Name == grouping.Key))
          {
            foreach (string outcon in grouping.Value)
            {
              if (!connections.Any(con => con.Name == outcon))
              {
                DebugConsole.ThrowError($"Error in item \"{Item.Name}/{Name}\" - matching out-connection not found for in-connection \"{grouping.Key}\" (expecting \"{outcon}\").");
              }
            }
          }
          else
          {
            foreach (string outcon in grouping.Value)
            {
               if (connections.Any(con => con.Name == outcon))
               {
                 DebugConsole.ThrowError($"Error in item \"{Name}\" - matching in-connection not found for out-connection \"{outcon}\" (expecting \"{grouping.Key}\").");
               }
            }
          }
        }
      }
    }

    public override void Update(float deltaTime, Camera cam)
    {
      item.SendSignal(CurrentChannel.ToString(), "state_out");

      ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
    }

    public override void ReceiveSignal(Signal signal, Connection connection)
    {
      if (item.Condition <= 0.0f) { return; }

      switch (connection.Name)
      {
        case "prev_state":
          if (signal.value == "0") { return; }
          SetChannel(CurrentChannel == MinChannel ? MaxChannel : CurrentChannel - 1);
          break;
        case "next_state":
          if (signal.value == "0") { return; }
          SetChannel(CurrentChannel == MaxChannel ? MinChannel : CurrentChannel + 1);
          break;
        case "set_state":
          if (Int32.TryParse(signal.value, out int target))
          {
            SetChannel(target);
          }
          break;
        default:
          if (conGroupings.TryGetValue(connection.Name, out List<string> outcons))
          {
            item.SendSignal(signal, outcons[CurrentChannel]);
          }
          break;
      }
    }

    public void SetChannel(int channel, bool isNetworkMessage = false)
    {
      CurrentChannel = channel;
    }
  }
}
