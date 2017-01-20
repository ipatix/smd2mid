using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FileMIDI
{
    public class MidiFile
    {
        public ushort TimeDivision;

        public List<MidiTrack> MidiTracks;

        public MidiFile()
        {
            MidiTracks = new List<MidiTrack>();
            TimeDivision = 0;
        }

        public MidiFile(ushort division)
        {
            MidiTracks = new List<MidiTrack>();
            TimeDivision = division;
        }

        public void LoadMidiFromFile(string filePath)
        {
            // first clear the currently loaded Midi by clearing the List
            bool succeeded = MidiLoader.LoadFromFile(filePath, ref MidiTracks, ref TimeDivision);      // load the Midi using the MidiLoader class
        }

        public void SaveMidiToFile(string filePath)
        {
            MidiSaver.SaveToFile(filePath, ref MidiTracks, TimeDivision);         // save the Midi to a file
        }

        public void SortTrackEvents(/*ref List<MidiTrack> sortingTracks*/)
        {
            for (int currentTrack = 0; currentTrack < MidiTracks.Count; currentTrack++)
            {
                MidiTracks[currentTrack].MidiEvents = MidiTracks[currentTrack].MidiEvents.OrderBy(item => item.GetAbsoluteTicks()).ToList();
            }
        }
    }

    public class MidiEvent
    {
        protected long AbsoluteTicks;

        public void SetAbsoluteTicks(long ticks)
        {
            AbsoluteTicks = ticks;
        }

        public long GetAbsoluteTicks()
        {
            return AbsoluteTicks;
        }

        virtual public byte[] GetEventData() 
        {
            throw new NotImplementedException();
        }
    }

    public class MidiTrack
    {
        public List<MidiEvent> MidiEvents;

        public MidiTrack()
        {
            MidiEvents = new List<MidiEvent>();
        }

        public bool ContainsProg(byte prog) 
        {
            foreach (MidiEvent ev in MidiEvents)
            {
                if (ev is MidiMessage)
                {
                    if (prog == (ev as MidiMessage).GetProg())
                        return true;
                }
            }
            return false;
        }

        public void SetChannel(byte chn)
        {
            foreach (MidiEvent ev in MidiEvents)
            {
                if (ev is MidiMessage)
                {
                    (ev as MidiMessage).SetChn(chn);
                }
            }
        }

        public bool ContainsNotes()
        {
            foreach (MidiEvent ev in MidiEvents)
            {
                if (ev is MidiMessage)
                {
                    if ((ev as MidiMessage).GetEvType() == NormalType.NoteOn)
                        return true;
                }
            }
            return false;
        }
    }

    public class MidiMessage : MidiEvent
    {
        private byte _midiChannel;
        private byte _parameter1;
        private byte _parameter2;
        private NormalType _type;

        public MidiMessage(long absTicks, byte _midiChannel, byte par1, byte par2, NormalType _type)
        {
            AbsoluteTicks = absTicks;
            this._midiChannel = _midiChannel;
            _parameter1 = par1;
            _parameter2 = par2;
            this._type = _type;
        }

        public int GetProg()
        {
            if (_type != NormalType.Program)
                return -1;
            return _parameter1;
        }
        
        public byte GetChn()
        {
            return _midiChannel;
        }

        public NormalType GetEvType()
        {
            return _type;
        }

        public void SetChn(byte chn)
        {
            chn &= 0xF;
            _midiChannel = chn;
        }

        override public byte[] GetEventData()
        {
            byte[] returnData = new byte[3];
            switch (_type)
            {
                case NormalType.NoteOff:                // #0x8
                    returnData[0] = (byte)(_midiChannel | (0x8 << 4));
                    returnData[1] = _parameter1;     // note number
                    returnData[2] = _parameter2;     // velocity
                    break;
                case NormalType.NoteOn:                 // #0x9
                    returnData[0] = (byte)(_midiChannel | (0x9 << 4));
                    returnData[1] = _parameter1;     // note number
                    returnData[2] = _parameter2;     // velocity
                    break;
                case NormalType.NoteAftertouch:         // #0xA
                    returnData[0] = (byte)(_midiChannel | (0xA << 4));
                    returnData[1] = _parameter1;     // note number
                    returnData[2] = _parameter2;     // aftertouch value
                    break;
                case NormalType.Controller:             // #0xB
                    returnData[0] = (byte)(_midiChannel | (0xB << 4));
                    returnData[1] = _parameter1;     // controller number
                    returnData[2] = _parameter2;     // controller value
                    break;
                case NormalType.Program:                // #0xC
                    returnData = new byte[2];           // this event doesn't have a 2nd parameter
                    returnData[0] = (byte)(_midiChannel | (0xC << 4));
                    returnData[1] = _parameter1;     // program number
                    break;
                case NormalType.ChannelAftertouch:      // #0xD
                    returnData = new byte[2];           // this event doesn't have a 2nd parameter
                    returnData[0] = (byte)(_midiChannel | (0xD << 4));
                    returnData[1] = _parameter1;     // aftertouch value
                    break;
                case NormalType.PitchBend:              // #0xE
                    returnData[0] = (byte)(_midiChannel | (0xE << 4));
                    returnData[1] = _parameter1;     // pitch LSB
                    returnData[2] = _parameter2;     // pitch MSB
                    break;
            }
            return returnData;
        }
    }

    public class MetaMessage : MidiEvent
    {
        private byte[] _data;
        private byte _metaType;

        override public byte[] GetEventData()     // returns a raw byte array of this META Event in the MIDI file
        {
            byte[] dataLength = VariableLength.ConvertToVariableLength(_data.Length);
            byte[] returnData = new byte[_data.Length + 2 + dataLength.Length];
            returnData[0] = 0xFF;
            returnData[1] = _metaType;
            Array.Copy(dataLength, 0, returnData, 2, dataLength.Length);
            Array.Copy(_data, 0, returnData, 2 + dataLength.Length, _data.Length);
            return returnData;
        }

        public MetaMessage(long ticks, byte _metaType, byte[] _data)
        {
            AbsoluteTicks = ticks;
            this._metaType = _metaType;
            this._data = _data;
        }
    }

    public class SysExMessage : MidiEvent
    {
        private byte[] _data;
        private byte _sysexType;

        override public byte[] GetEventData()     // returns a raw byte array of this SysEx Event in the MIDI file
        {
            byte[] dataLength = VariableLength.ConvertToVariableLength(_data.Length);
            byte[] returnData = new byte[_data.Length + 1 + dataLength.Length];
            returnData[0] = _sysexType;
            Array.Copy(dataLength, 0, returnData, 1, dataLength.Length);
            Array.Copy(_data, 0, returnData, 1 + dataLength.Length, _data.Length);
            return returnData;
        }

        public SysExMessage(long ticks, byte _sysexType, byte[] _data)
        {
            AbsoluteTicks = ticks;
            this._sysexType = _sysexType;
            this._data = _data;
        }
    }

    public enum EventType
    {
        Normal,
        Meta, 
        SysEx
    }

    public enum NormalType
    {
        NoteOn, 
        NoteOff, 
        NoteAftertouch, 
        Controller, 
        Program, 
        ChannelAftertouch, 
        PitchBend
    }

    public static class VariableLength
    {
        public static byte[] ConvertToVariableLength(long value)
        {
            int i = 0;
            byte[] returnData = new byte[i + 1];
            returnData[i] = (byte)(value & 0x7F);
            i++;

            value = value >> 7;

            while (value != 0)
            {
                Array.Resize(ref returnData, i + 1);
                returnData[i] = (byte)((value & 0x7F) | 0x80);
                value = value >> 7;
                i++;
            }

            Array.Reverse(returnData);
            return returnData;
        }

        public static long ConvertToInt(byte[] values)
        {
            long value = 0;
            for (int i = 0; i < values.Length; i++)
            {
                value = value << 7;     // doesn't matter on first loop anyway, if it's one of the next loops it shifts the latest value up 7 bits
                value = value | (byte)(values[i] & 0x7F);
            }
            return value;
        }
    }
}
