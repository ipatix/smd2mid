using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace FileMIDI
{
    static class MidiLoader
    {
        public static bool LoadFromFile(string filePath, ref List<MidiTrack> midiTracks, ref ushort timeDivision)
        {
            midiTracks.Clear();     // remove all elements currently loaded
            // check if the Midi is actually a Midi file
            Console.WriteLine("Verifying MIDI file...");
            VerifyMidi(filePath);   // this will raise an exception if the Midi file is invalid
            Console.WriteLine("Identify MIDI file type...");
            int midiType = GetMidiType(filePath);
            Console.WriteLine("The MIDI file type is #{0}", midiType);

            bool success = false;

            switch (midiType)
            {
                case 0:
                    Console.WriteLine("Converting and loading MIDI...");
                    LoadAndConvertTypeZero(filePath, ref midiTracks, ref timeDivision);
                    success = true;
                    break;
                case 1:
                    Console.WriteLine("Loading MIDI...");
                    LoadDirectly(filePath, ref midiTracks, ref timeDivision);
                    success = true;
                    break;
                case 2:
                    Console.WriteLine("MIDI type 2 is not supported by this program, nor it is by mid2agb!");
                    break;
                default:
                    Console.WriteLine("Invalid MIDI type!");
                    break;
            }

            return success;
        }

        private static void LoadDirectly(string filePath, ref List<MidiTrack> midiTracks, ref ushort timeDivision)              // returns the MIDI loaded in the List of all individual tracks
        {
            // FileStreams seem to have their own buffering layer so there is no need for an additional Buffered Stream
            FileStream midiFileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            BinaryReader midiBinaryStream = new BinaryReader(midiFileStream);
            midiFileStream.Position = 0xA;      // seek to the amount of tracks in the MIDI file
            int numTracks = midiBinaryStream.ReadByte() << 8 | midiBinaryStream.ReadByte();
            timeDivision = (ushort)(midiBinaryStream.ReadByte() << 8 | midiBinaryStream.ReadByte());
            // finished reading the header data, now continue transscribing the tracks

            for (int currentTrack = 0; currentTrack < numTracks; currentTrack++)
            {
                midiTracks.Add(new MidiTrack());     // we have to create the object of the track first and we can add it later to out track list if the track was transscribed into it's objects
                long currentTick = 0;
                NormalType lastEventType = NormalType.NoteOff;
                byte lastMidiChannel = 0;
                // check if the track doesn't begin like expected with an MTrk string
                byte[] textString = new byte[4];
                midiBinaryStream.Read(textString, 0, 4);
                if (Encoding.ASCII.GetString(textString, 0, 4) != "MTrk") throw new Exception("Track doesn't start with MTrk string!");
                byte[] intArray = new byte[4];
                midiBinaryStream.Read(intArray, 0, 4);    // read the track length
                // this value isn't even needed, so we don't do further processing with it; I left it in the code for some usage in the future; no specific plan???

                // now do the event loop and load all the events
                #region EventLoop
                while (true)
                {
                    // first thing that is done is getting the next delta length value and add the value to the current position to calculate the absolute position of the event
                    currentTick += ReadVariableLengthValue(ref midiBinaryStream);
                    
                    // now check what event type is used and disassemble it

                    byte eventTypeByte = midiBinaryStream.ReadByte();

                    // do a jumptable for each event type

                    if (eventTypeByte == 0xFF)      // if META Event
                    {
                        byte metaType = (byte)midiFileStream.ReadByte();
                        long metaLength = ReadVariableLengthValue(ref midiBinaryStream);
                        byte[] metaData = new byte[metaLength];
                        midiBinaryStream.Read(metaData, 0, (int)metaLength);

                        if (metaType == 0x2F) break;        // if end of track is reached, break out of the loop, End of Track Events aren't written into the objects

                        midiTracks[currentTrack].MidiEvents.Add(new MetaMessage(currentTick, metaType, metaData)); // new MidiEvent(currentTick, metaType, metaData, true

                    }
                    else if (eventTypeByte == 0xF0 || eventTypeByte == 0xF7)        // if SysEx Event
                    {
                        long sysexLength = ReadVariableLengthValue(ref midiBinaryStream);
                        byte[] sysexData = new byte[sysexLength];
                        midiBinaryStream.Read(sysexData, 0, (int)sysexLength);
                        midiTracks[currentTrack].MidiEvents.Add(new SysExMessage(currentTick, eventTypeByte, sysexData)); // new MidiEvent(currentTick, eventTypeByte, sysexData, false)
                    }
                    else if (eventTypeByte >> 4 == 0x8)     // if Note OFF command
                    {
                        byte par1 = midiBinaryStream.ReadByte();
                        byte par2 = midiBinaryStream.ReadByte();
                        midiTracks[currentTrack].MidiEvents.Add(new MidiMessage(currentTick, (byte)(eventTypeByte & 0xF), par1, par2, NormalType.NoteOff)); // new MidiEvent(currentTick, (byte)(eventTypeByte & 0xF), par1, par2, NormalType.NoteOFF)
                        // save the last event type and channel
                        lastEventType = NormalType.NoteOff;
                        lastMidiChannel = (byte)(eventTypeByte & 0xF);
                    }
                    else if (eventTypeByte >> 4 == 0x9)     // if Note ON command
                    {
                        byte par1 = midiBinaryStream.ReadByte();
                        byte par2 = midiBinaryStream.ReadByte();
                        midiTracks[currentTrack].MidiEvents.Add(new MidiMessage(currentTick, (byte)(eventTypeByte & 0xF), par1, par2, NormalType.NoteOn));
                        // save the last event type and channel
                        lastEventType = NormalType.NoteOn;
                        lastMidiChannel = (byte)(eventTypeByte & 0xF);
                    }
                    else if (eventTypeByte >> 4 == 0xA)     // if Aftertouch command
                    {
                        byte par1 = midiBinaryStream.ReadByte();
                        byte par2 = midiBinaryStream.ReadByte();
                        midiTracks[currentTrack].MidiEvents.Add(new MidiMessage(currentTick, (byte)(eventTypeByte & 0xF), par1, par2, NormalType.NoteAftertouch));
                        // save the last event type and channel
                        lastEventType = NormalType.NoteAftertouch;
                        lastMidiChannel = (byte)(eventTypeByte & 0xF);
                    }
                    else if (eventTypeByte >> 4 == 0xB)     // if MIDI controller command
                    {
                        byte par1 = midiBinaryStream.ReadByte();
                        byte par2 = midiBinaryStream.ReadByte();
                        midiTracks[currentTrack].MidiEvents.Add(new MidiMessage(currentTick, (byte)(eventTypeByte & 0xF), par1, par2, NormalType.Controller));
                        // save the last event type and channel
                        lastEventType = NormalType.Controller;
                        lastMidiChannel = (byte)(eventTypeByte & 0xF);
                    }
                    else if (eventTypeByte >> 4 == 0xC)     // if Preset command
                    {
                        byte par1 = midiBinaryStream.ReadByte();
                        byte par2 = 0x0;    // unused
                        midiTracks[currentTrack].MidiEvents.Add(new MidiMessage(currentTick, (byte)(eventTypeByte & 0xF), par1, par2, NormalType.Program));
                        // save the last event type and channel
                        lastEventType = NormalType.Program;
                        lastMidiChannel = (byte)(eventTypeByte & 0xF);
                    }
                    else if (eventTypeByte >> 4 == 0xD)     // if Channel Aftertouch command
                    {
                        byte par1 = midiBinaryStream.ReadByte();
                        byte par2 = 0x0;    // unused
                        midiTracks[currentTrack].MidiEvents.Add(new MidiMessage(currentTick, (byte)(eventTypeByte & 0xF), par1, par2, NormalType.ChannelAftertouch));
                        // save the last event type and channel
                        lastEventType = NormalType.ChannelAftertouch;
                        lastMidiChannel = (byte)(eventTypeByte & 0xF);
                    }
                    else if (eventTypeByte >> 4 == 0xE)     // if Pitch Bend command
                    {
                        byte par1 = midiBinaryStream.ReadByte();
                        byte par2 = midiBinaryStream.ReadByte();
                        midiTracks[currentTrack].MidiEvents.Add(new MidiMessage(currentTick, (byte)(eventTypeByte & 0xF), par1, par2, NormalType.PitchBend));
                        // save the last event type and channel
                        lastEventType = NormalType.PitchBend;
                        lastMidiChannel = (byte)(eventTypeByte & 0xF);
                    }
                    else if (eventTypeByte >> 4 < 0x8)
                    {
                        byte par1 = eventTypeByte;
                        byte par2;
                        switch (lastEventType)
                        {
                            case NormalType.NoteOff:
                                par2 = midiBinaryStream.ReadByte();
                                midiTracks[currentTrack].MidiEvents.Add(new MidiMessage(currentTick, lastMidiChannel, par1, par2, NormalType.NoteOff));
                                break;
                            case NormalType.NoteOn:
                                par2 = midiBinaryStream.ReadByte();
                                midiTracks[currentTrack].MidiEvents.Add(new MidiMessage(currentTick, lastMidiChannel, par1, par2, NormalType.NoteOn));
                                break;
                            case NormalType.NoteAftertouch:
                                par2 = midiBinaryStream.ReadByte();
                                midiTracks[currentTrack].MidiEvents.Add(new MidiMessage(currentTick, lastMidiChannel, par1, par2, NormalType.NoteAftertouch));
                                break;
                            case NormalType.Controller:
                                par2 = midiBinaryStream.ReadByte();
                                midiTracks[currentTrack].MidiEvents.Add(new MidiMessage(currentTick, lastMidiChannel, par1, par2, NormalType.Controller));
                                break;
                            case NormalType.Program:
                                midiTracks[currentTrack].MidiEvents.Add(new MidiMessage(currentTick, lastMidiChannel, par1, 0x0, NormalType.Program));
                                break;
                            case NormalType.ChannelAftertouch:
                                midiTracks[currentTrack].MidiEvents.Add(new MidiMessage(currentTick, lastMidiChannel, par1, 0x0, NormalType.ChannelAftertouch));
                                break;
                            case NormalType.PitchBend:
                                par2 = midiBinaryStream.ReadByte();
                                midiTracks[currentTrack].MidiEvents.Add(new MidiMessage(currentTick, lastMidiChannel, par1, par2, NormalType.PitchBend));
                                break;
                        }
                    }
                    else
                    {
                        throw new Exception("Bad MIDI event at 0x" + midiBinaryStream.BaseStream.Position.ToString("X8") + ": 0x" + eventTypeByte.ToString("X2"));
                    }
                }   // end of the event transscribing loop
                #endregion
            }   // end of the track loop
            midiBinaryStream.BaseStream.Close();
        }   // end of function loadDirectly

        private static void LoadAndConvertTypeZero(string filePath, ref List<MidiTrack> midiTracks, ref ushort timeDivision)    // returns the MIDI loaded in the List of all individual MIDI channels split up into 16 tracks
        {
            // FileStreams seem to have their own buffering layer so there is no need for an additional Buffered Stream
            FileStream midiFileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            BinaryReader midiBinaryStream = new BinaryReader(midiFileStream);
            midiFileStream.Position = 0xC;      // seek to the amount of tracks in the MIDI file
            timeDivision = (ushort)(midiBinaryStream.ReadByte() << 8 | midiBinaryStream.ReadByte());
            // finished reading the header data, now continue transscribing the single track to multiple ones, depending on the channel

            for (int i = 0; i < 16; i++) midiTracks.Add(new MidiTrack());     // we have to create tracks for each MIDI channel (i.e. 16)
            long currentTick = 0;
            NormalType lastEventType = NormalType.NoteOff;
            byte lastMidiChannel = 0;
            // check if the track doesn't begin like expected with an MTrk string
            byte[] textString = new byte[4];
            midiBinaryStream.Read(textString, 0, 4);
            if (Encoding.ASCII.GetString(textString, 0, 4) != "MTrk") throw new Exception("Track doesn't start with MTrk string!");
            byte[] intArray = new byte[4];
            midiBinaryStream.Read(intArray, 0, 4);    // read the track length
            // this value isn't even needed, so we don't do further processing with it; I left it in the code for some usage in the future; no specific plan???

            // now do the event loop and load all the events and remap the channels
            #region EventLoop
            while (true)
            {
                // first thing that is done is getting the next delta length value and add the value to the current position to calculate the absolute position of the event
                currentTick += ReadVariableLengthValue(ref midiBinaryStream);

                // now check what event type is used and disassemble it

                byte eventTypeByte = midiBinaryStream.ReadByte();

                // do a jumptable for each event type

                if (eventTypeByte == 0xFF)      // if META Event
                {
                    byte metaType = (byte)midiFileStream.ReadByte();
                    long metaLength = ReadVariableLengthValue(ref midiBinaryStream);
                    byte[] metaData = new byte[metaLength];
                    midiBinaryStream.Read(metaData, 0, (int)metaLength);

                    if (metaType == 0x2F) break;        // End of track events aren't loaded into the objects

                    midiTracks[0].MidiEvents.Add(new MetaMessage(currentTick, metaType, metaData));
                }
                else if (eventTypeByte == 0xF0 || eventTypeByte == 0xF7)        // if SysEx Event
                {
                    long sysexLength = ReadVariableLengthValue(ref midiBinaryStream);
                    byte[] sysexData = new byte[sysexLength];
                    midiBinaryStream.Read(sysexData, 0, (int)sysexLength);
                    midiTracks[0].MidiEvents.Add(new SysExMessage(currentTick, eventTypeByte, sysexData));
                }
                else if (eventTypeByte >> 4 == 0x8)     // if Note OFF command
                {
                    byte par1 = midiBinaryStream.ReadByte();
                    byte par2 = midiBinaryStream.ReadByte();
                    midiTracks[eventTypeByte & 0xF].MidiEvents.Add(new MidiMessage(currentTick, (byte)(eventTypeByte & 0xF), par1, par2, NormalType.NoteOff));
                    // now backup channel and Normal Type for truncated commands
                    lastEventType = NormalType.NoteOff;
                    lastMidiChannel = (byte)(eventTypeByte & 0xF);
                }
                else if (eventTypeByte >> 4 == 0x9)     // if Note ON command
                {
                    byte par1 = midiBinaryStream.ReadByte();
                    byte par2 = midiBinaryStream.ReadByte();
                    midiTracks[eventTypeByte & 0xF].MidiEvents.Add(new MidiMessage(currentTick, (byte)(eventTypeByte & 0xF), par1, par2, NormalType.NoteOn));
                    // now backup channel and Normal Type for truncated commands
                    lastEventType = NormalType.NoteOn;
                    lastMidiChannel = (byte)(eventTypeByte & 0xF);
                }
                else if (eventTypeByte >> 4 == 0xA)     // if Aftertouch command
                {
                    byte par1 = midiBinaryStream.ReadByte();
                    byte par2 = midiBinaryStream.ReadByte();
                    midiTracks[eventTypeByte & 0xF].MidiEvents.Add(new MidiMessage(currentTick, (byte)(eventTypeByte & 0xF), par1, par2, NormalType.NoteAftertouch));
                    // now backup channel and Normal Type for truncated commands
                    lastEventType = NormalType.NoteAftertouch;
                    lastMidiChannel = (byte)(eventTypeByte & 0xF);
                }
                else if (eventTypeByte >> 4 == 0xB)     // if MIDI controller command
                {
                    byte par1 = midiBinaryStream.ReadByte();
                    byte par2 = midiBinaryStream.ReadByte();
                    midiTracks[eventTypeByte & 0xF].MidiEvents.Add(new MidiMessage(currentTick, (byte)(eventTypeByte & 0xF), par1, par2, NormalType.Controller));
                    // now backup channel and Normal Type for truncated commands
                    lastEventType = NormalType.Controller;
                    lastMidiChannel = (byte)(eventTypeByte & 0xF);
                }
                else if (eventTypeByte >> 4 == 0xC)     // if Preset command
                {
                    byte par1 = midiBinaryStream.ReadByte();
                    byte par2 = 0x0;
                    midiTracks[eventTypeByte & 0xF].MidiEvents.Add(new MidiMessage(currentTick, (byte)(eventTypeByte & 0xF), par1, par2, NormalType.Program));
                    // now backup channel and Normal Type for truncated commands
                    lastEventType = NormalType.Program;
                    lastMidiChannel = (byte)(eventTypeByte & 0xF);
                }
                else if (eventTypeByte >> 4 == 0xD)     // if Channel Aftertouch command
                {
                    byte par1 = midiBinaryStream.ReadByte();
                    byte par2 = 0x0;
                    midiTracks[eventTypeByte & 0xF].MidiEvents.Add(new MidiMessage(currentTick, (byte)(eventTypeByte & 0xF), par1, par2, NormalType.ChannelAftertouch));
                    // now backup channel and Normal Type for truncated commands
                    lastEventType = NormalType.ChannelAftertouch;
                    lastMidiChannel = (byte)(eventTypeByte & 0xF);
                }
                else if (eventTypeByte >> 4 == 0xE)     // if Pitch Bend command
                {
                    byte par1 = midiBinaryStream.ReadByte();
                    byte par2 = midiBinaryStream.ReadByte();
                    midiTracks[eventTypeByte & 0xF].MidiEvents.Add(new MidiMessage(currentTick, (byte)(eventTypeByte & 0xF), par1, par2, NormalType.PitchBend));
                    // now backup channel and Normal Type for truncated commands
                    lastEventType = NormalType.PitchBend;
                    lastMidiChannel = (byte)(eventTypeByte & 0xF);
                }
                else if (eventTypeByte >> 4 < 0x8)
                {
                    byte par1 = eventTypeByte;
                    byte par2;
                    switch (lastEventType)
                    {
                        case NormalType.NoteOff:
                            par2 = midiBinaryStream.ReadByte();
                            midiTracks[lastMidiChannel].MidiEvents.Add(new MidiMessage(currentTick, lastMidiChannel, par1, par2, NormalType.NoteOff));
                            break;
                        case NormalType.NoteOn:
                            par2 = midiBinaryStream.ReadByte();
                            midiTracks[lastMidiChannel].MidiEvents.Add(new MidiMessage(currentTick, lastMidiChannel, par1, par2, NormalType.NoteOn));
                            break;
                        case NormalType.NoteAftertouch:
                            par2 = midiBinaryStream.ReadByte();
                            midiTracks[lastMidiChannel].MidiEvents.Add(new MidiMessage(currentTick, lastMidiChannel, par1, par2, NormalType.NoteAftertouch));
                            break;
                        case NormalType.Controller:
                            par2 = midiBinaryStream.ReadByte();
                            midiTracks[lastMidiChannel].MidiEvents.Add(new MidiMessage(currentTick, lastMidiChannel, par1, par2, NormalType.Controller));
                            break;
                        case NormalType.Program:
                            midiTracks[lastMidiChannel].MidiEvents.Add(new MidiMessage(currentTick, lastMidiChannel, par1, 0x0, NormalType.Program));
                            break;
                        case NormalType.ChannelAftertouch:
                            midiTracks[lastMidiChannel].MidiEvents.Add(new MidiMessage(currentTick, lastMidiChannel, par1, 0x0, NormalType.ChannelAftertouch));
                            break;
                        case NormalType.PitchBend:
                            par2 = midiBinaryStream.ReadByte();
                            midiTracks[lastMidiChannel].MidiEvents.Add(new MidiMessage(currentTick, lastMidiChannel, par1, par2, NormalType.PitchBend));
                            break;
                    }
                }
                else
                {
                    throw new Exception("Bad MIDI event at 0x" + midiBinaryStream.BaseStream.Position.ToString("X8") + ": 0x" + eventTypeByte.ToString("X2"));
                }
            }
            #endregion
            midiFileStream.Close();
        }

        private static void VerifyMidi(string filePath)     // throws an Exception if a BAD file was selected ; FINISHED
        {
            FileStream midiFileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] midiHeaderString = new byte[4];
            midiFileStream.Position = 0;
            midiFileStream.Read(midiHeaderString, 0, 4);
            if (Encoding.ASCII.GetString(midiHeaderString, 0, 4) != "MThd") throw new Exception("MThd string wasn't found in the MIDI header!");
            if (midiFileStream.ReadByte() != 0x0 || midiFileStream.ReadByte() != 0x0 || midiFileStream.ReadByte() != 0x0 || midiFileStream.ReadByte() != 0x6) throw new Exception("MThd chunk size not #0x6!");
            midiFileStream.Position = 0xA;
            int numTracks = midiFileStream.ReadByte() << 8 | midiFileStream.ReadByte();
            if (numTracks == 0) throw new Exception("The MIDI has no tracks to convert!");
            midiFileStream.Close();
        }

        private static int GetMidiType(string filePath)     // returns the MIDI type automonously ; FINISHED
        {
            FileStream midiFileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            midiFileStream.Position = 9;    // position to the midi Type
            int returnValue = midiFileStream.ReadByte();
            midiFileStream.Close();
            return returnValue;
        }

        public static byte[] UshortToBigEndian(ushort value)   // returns a byte Array in Big Endian of an ushort ; FINISHED
        {
            byte[] dataValues = new byte[2];
            dataValues[0] = (byte)(value >> 8);
            dataValues[1] = (byte)(value & 0xFF);
            return dataValues;
        }

        public static byte[] IntToBigEndian(int value)         // returns a byte Array in Big Endian of an int ; FINISHED
        {
            byte[] dataValues = new byte[4];
            dataValues[0] = (byte)(value >> 24);
            dataValues[1] = (byte)((value >> 16) & 0xFF);
            dataValues[2] = (byte)((value >> 8) & 0xFF);
            dataValues[3] = (byte)(value & 0xFF);
            return dataValues;
        }

        public static ushort LoadBigEndianUshort(byte[] dataValues)    // returns an ushort by the Big Endian values in the Array ; FINISHED
        {
            return (ushort)(dataValues[0] << 8 | dataValues[1]);
        }

        public static int LoadBigEndianInt(byte[] dataValues)       // returns an int by the Big Endian values in the Array ; FINISHED
        {
            return dataValues[0] << 24 | dataValues[1] << 16 | dataValues[2] << 8 | dataValues[3];
        }

        public static long ReadVariableLengthValue(ref BinaryReader midiBinaryStream)     // reads a variable Length value from the Filestream at its current position and extends the Stream position by the exact amount of bytes ; FINSHED
        {
            long backupPosition = midiBinaryStream.BaseStream.Position;
            int numBytes = 0;
            while (true)
            {
                numBytes++;
                if ((midiBinaryStream.ReadByte() & 0x80) == 0) break;
            }

            midiBinaryStream.BaseStream.Position = backupPosition;

            long returnValue = 0;

            for (int currentByte = 0; currentByte < numBytes; currentByte++)
            {
                returnValue = (returnValue << 7) | (byte)(midiBinaryStream.ReadByte() & 0x7F);
            }

            return returnValue;
        }
    }
}
