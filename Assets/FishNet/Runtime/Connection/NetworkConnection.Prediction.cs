#if UNITY_EDITOR || DEVELOPMENT_BUILD
#define DEVELOPMENT
#endif
using FishNet.Managing;
using FishNet.Managing.Predicting;
using FishNet.Managing.Timing;
using FishNet.Managing.Transporting;
using FishNet.Serializing;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FishNet.Connection
{
    /// <summary>
    /// A container for a connected client used to perform actions on and gather information for the declared client.
    /// </summary>
    public partial class NetworkConnection
    {
        /// <summary>
        /// Approximate replicate tick on the server for this connection.
        /// This also contains the last set value for local and remote.
        /// </summary>
        public EstimatedTick ReplicateTick { get; private set; } = new();
        /// <summary>
        /// Fewest replicates the server held queued for this connection's objects after a tick, over the timing interval it last sent this connection.
        /// This is the input delay the connection's replicates stood at the whole interval, which jitter did not use. 0 until a timing update is sent, and while the connection's replicates are not queued.
        /// Only set on the server.
        /// </summary>
        public byte ReplicateQueueFloor { get; private set; }
        /// <summary>
        /// Fewest replicates queued after a tick since the last timing update, or UNSET_REPLICATE_QUEUE when none was sampled.
        /// </summary>
        private int _replicateQueueLowest = UNSET_REPLICATE_QUEUE;
        /// <summary>
        /// Value of _replicateQueueLowest when no queue was sampled.
        /// </summary>
        private const int UNSET_REPLICATE_QUEUE = int.MaxValue;
        /// <summary>
        /// Writers for states.
        /// </summary>
        internal List<PooledWriter> PredictionStateWriters = new();
        internal void Prediction_Initialize(NetworkManager manager, bool asServer) { }

        /// <summary>
        /// Records how many replicates one of this connection's objects still has queued after the server ran a tick.
        /// </summary>
        internal void SampleReplicateQueue(int queued)
        {
            if (queued < _replicateQueueLowest)
                _replicateQueueLowest = queued;
        }

        /// <summary>
        /// Ends a timing interval: sets ReplicateQueueFloor from the queues sampled in it and starts the next.
        /// </summary>
        /// <returns>The floor, to send with the timing update.</returns>
        internal byte TakeReplicateQueueFloor()
        {
            ReplicateQueueFloor = _replicateQueueLowest == UNSET_REPLICATE_QUEUE ? (byte)0 : (byte)Math.Min(_replicateQueueLowest, byte.MaxValue);
            _replicateQueueLowest = UNSET_REPLICATE_QUEUE;
            return ReplicateQueueFloor;
        }

        /// <summary>
        /// Writes a prediction state.
        /// </summary>
        /// <param name = "data"></param>
        internal void WriteState(PooledWriter data)
        {
            #if !DEVELOPMENT
            // Do not send states to clientHost.
            if (IsLocalClient)
                return;
            #endif

            TimeManager timeManager = NetworkManager.TimeManager;
            TransportManager transportManager = NetworkManager.TransportManager;
            uint ticksBehind = IsLocalClient ? 0 : PacketTick.LocalTickDifference(timeManager);
            /* If it's been a really long while the client could just be setting up
             * or dropping. Only send if they've communicated within 5 seconds. */
            if (ticksBehind > timeManager.TickRate * 5)
                return;

            int mtu = transportManager.GetLowestMTU((byte)Channel.Unreliable);
            PooledWriter stateWriter;
            int writerCount = PredictionStateWriters.Count;

            //If there are no writers then get a new writer.
            if (writerCount == 0 || data.Length > mtu)
            {
                AddNewStateWriter();
            }
            /* If a current writer exist and
             * if the data length + existing written data will exceed
             * MTU then get a new writer. */
            else
            {
                int lengthInCurrentWriter = PredictionStateWriters[writerCount - 1].Length;
                int totalLength = lengthInCurrentWriter + data.Length;
                
                if (totalLength > mtu)
                    AddNewStateWriter();
            }

            void AddNewStateWriter()
            {
                stateWriter = WriterPool.Retrieve(mtu);
                PredictionStateWriters.Add(stateWriter);
                stateWriter.Skip(PredictionManager.STATE_HEADER_RESERVE_LENGTH);
                /// 2 PacketId.
                /// 4 Last replicate tick run for connection.
                /// 4 Length unpacked.

                writerCount++;
            }

            stateWriter = PredictionStateWriters[writerCount - 1];
            stateWriter.WriteArraySegment(data.GetArraySegment());
        }

        /// <summary>
        /// Stores prediction writers to be re-used later.
        /// </summary>
        internal void StorePredictionStateWriters()
        {
            for (int i = 0; i < PredictionStateWriters.Count; i++)
                WriterPool.Store(PredictionStateWriters[i]);

            PredictionStateWriters.Clear();
        }

        /// <summary>
        /// Sets the last tick a NetworkBehaviour replicated with.
        /// </summary>
        /// <param name = "setUnordered">True to set unordered value, false to set ordered.</param>
        internal void SetReplicateTick(uint value, EstimatedTick.OldTickOption oldTickOption = EstimatedTick.OldTickOption.Discard)
        {
            ReplicateTick.Update(value, oldTickOption);
        }

        /// <summary>
        /// Resets NetworkConnection.
        /// </summary>
        private void Prediction_Reset()
        {
            StorePredictionStateWriters();
            ReplicateTick.Reset();
            ReplicateQueueFloor = 0;
            _replicateQueueLowest = UNSET_REPLICATE_QUEUE;
        }
    }
}