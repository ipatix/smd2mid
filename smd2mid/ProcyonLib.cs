using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using FileMIDI;

namespace smd2mid
{
    class ProcyonSequenceLoader
    {
        private readonly byte[] _delayLut = { 96, 72, 64, 48, 36, 32, 24, 18, 16, 12, 9, 8, 6, 4, 3, 2 };

        private FileStream _smdFile;
        private BinaryReader _smdReader;
        private MidiFile _midiFile;
        private ProcyonSequenceInfo _smdInfo;

        byte[] midimap;
        byte[] drummap;
        byte[] transpose;

        public ProcyonSequenceLoader(string inputFile)
        {
            _smdFile = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            _smdReader = new BinaryReader(_smdFile);
            _midiFile = new MidiFile(96);

            byte[] identifierBytes = new byte[4];
            _smdFile.Position = 0;
            _smdFile.Read(identifierBytes, 0, 4);
            string identifier = Encoding.ASCII.GetString(identifierBytes);
            if (identifier != "smdl")
                throw new Exception("Bad file identifier. Did you really load a Procyon DSE sequence (.smd)?");
            // TODO do some more consistency checking

            _smdInfo = new ProcyonSequenceInfo(_smdFile);

            if (File.Exists("midimap.bin"))
            {
                midimap = File.ReadAllBytes("midimap.bin");
                if (midimap.Length != 0x80) midimap = generateMap();
            }
            else midimap = generateMap();
            if (File.Exists("drummap.bin"))
            {
                drummap = File.ReadAllBytes("drummap.bin");
                if (drummap.Length != 0x80) drummap = generateMap();
            }
            else drummap = generateMap();
            if (File.Exists("transpose.bin"))
            {
                transpose = File.ReadAllBytes("transpose.bin");
                if (transpose.Length != 0x80) transpose = generateStaticMap();
            }
            else transpose = generateStaticMap();
        }

        private static byte[] generateMap()
        {
            byte[] map = new byte[0x80];
            for (byte i = 0; i < 0x80; i++)
                map[i] = i;
            return map;
        }

        private static byte[] generateStaticMap()
        {
            byte[] map = new byte[0x80];
            for (int i = 0; i < 0x80; i++)
                map[i] = 0;
            return map;
        }

        public void WriteSequenceToFile(string path)
        {
            _midiFile.SaveMidiToFile(path);
        }

        private void FixMidiChannels()
        {
            byte currentChn = 0;
            foreach (MidiTrack tr in _midiFile.MidiTracks)
            {
                // check if the track contains notes
                bool isDrumTrack = tr.ContainsProg(127);
                bool containsNotes = tr.ContainsNotes();
                if (isDrumTrack)
                {
                    tr.SetChannel(9);
                }
                else
                {
                    tr.SetChannel(currentChn);
                    if (containsNotes)
                        currentChn++;
                    if (currentChn == 9)
                        currentChn++;
                }
            }
        }

        public void LoadSequence()
        {
            _midiFile.MidiTracks.Clear();
            int cMidiChn = 0;
            // process each track
            for (int currentTrack = 0; currentTrack < _smdInfo.NumTracks; currentTrack++)
            {
                long cTick = 0;
                int cOctave = 0;
                byte cProgram = 0;
                bool bendIsInit = false;
                int lastDelay = 0;
                int lastNoteLength = 1;

                MidiTrack cTrack;

                _midiFile.MidiTracks.Add(cTrack = new MidiTrack());

                if (currentTrack == 0)
                {
                    cTrack.MidiEvents.Add(
                        new MetaMessage(
                            0,
                            3, // sequence name
                            Encoding.ASCII.GetBytes(_smdInfo.SequenceName)
                            )
                        );
                }

                _smdReader.BaseStream.Position = _smdInfo.TrackOffset[currentTrack] + 0x14; // skip the first 4 bytes of the track data

                while (true)
                {
                    byte cmd = _smdReader.ReadByte();
                    if (cmd >= 0x0 && cmd <= 0x7F) // note
                    {
                        int noteArgument = _smdReader.ReadByte();
                        int octaveDelta = ((noteArgument >> 4) & 0x3) - 2;
                        cOctave += octaveDelta;
                        byte midiKey = (byte)(cOctave * 12 + (noteArgument & 0xF));
                        int numArgs = (noteArgument >> 6) & 0x3;
                        int noteLength = 0;
                        if (numArgs == 0)
                        {
                            noteLength = lastNoteLength;
                        }
                        else
                        {
                            for (int i = 0; i < numArgs; i++)
                            {
                                noteLength = (noteLength << 8) + _smdReader.ReadByte();
                            }
                            lastNoteLength = noteLength;
                        }
                        if (midiKey >= 0 && midiKey <= 0x7F)
                        {
                            cTrack.MidiEvents.Add(
                                    new MidiMessage(
                                        cTick,
                                        (byte)(cMidiChn & 0xF),
                                        (cProgram == 127) ? drummap[midiKey] : (byte)(midiKey + (sbyte)transpose[cProgram]),
                                        cmd,
                                        NormalType.NoteOn
                                        )
                                    );
                            cTrack.MidiEvents.Add(
                                    new MidiMessage(
                                        Math.Max(0, cTick + (noteLength * 2) - 1),
                                        (byte)(cMidiChn & 0xF),
                                        (cProgram == 127) ? drummap[midiKey] : (byte)(midiKey + (sbyte)transpose[cProgram]),
                                        0,
                                        NormalType.NoteOff
                                        )
                                    );

                        }
                        else
                        {
                            Console.WriteLine("Dropped invalid note event on track " + currentTrack);
                        }
                    }
                    else if (cmd >= 0x80 && cmd <= 0x8F) // if delay
                    {
                        long delayTo = cTick + (2 * (lastDelay = _delayLut[cmd - 0x80]));
                        cTick = delayTo;
                    }
                    else if (cmd == 0x90)
                    {
                        long delayTo = cTick + (lastDelay * 2);
                        cTick = delayTo;
                    }
                    else if (cmd == 0x91)
                    {
                        lastDelay += _smdReader.ReadSByte();
                        if (lastDelay < 0) lastDelay = 0;
                        long delayTo = cTick + (lastDelay * 2);
                        cTick = delayTo;
                    }
                    else if (cmd == 0x92)
                    {
                        long delayTo = cTick + (2 * (lastDelay = _smdReader.ReadByte()));
                        cTick = delayTo;
                    }
                    else if (cmd == 0x93)
                    {
                        int a = _smdReader.ReadByte();
                        int b = _smdReader.ReadByte();
                        long delayTo = cTick + (2 * (lastDelay = (a | (b << 8))));
                        cTick = delayTo;
                    }
                    else if (cmd == 0x98) // FINE | LOOP
                    {
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    1, // text event
                                    Encoding.ASCII.GetBytes("end")
                                    )
                                );
                        break;
                    }
                    else if (cmd == 0x99)
                    {
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    1, // text event
                                    Encoding.ASCII.GetBytes("loopStart")
                                    )
                                );
                    }
                    else if (cmd == 0x9C)
                    {
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    1, // text event
                                    Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":" + _smdReader.ReadByte().ToString("X2"))
                                    )
                                );
                    }
                    else if (cmd == 0x9D)
                    {
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    1, // text event
                                    Encoding.ASCII.GetBytes(cmd.ToString("X2"))
                                    )
                                );
                    }
                    else if (cmd == 0xA0) // set octave
                    {
                        cOctave = _smdReader.ReadByte();
                    }
                    else if (cmd == 0xA4)
                    {
                        byte bpm = _smdReader.ReadByte();
                        int microsPerBeat = 60 * 1000 * 1000 / bpm;
                        byte[] speed = new byte[3];
                        speed[0] = (byte)(microsPerBeat >> 16);
                        speed[1] = (byte)((microsPerBeat >> 8) & 0xFF);
                        speed[2] = (byte)(microsPerBeat & 0xFF);
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    0x51,
                                    speed
                                )
                            );
                    }
                    else if (cmd == 0xA8)
                    {
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    1, // text event
                                    Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":" + _smdReader.ReadByte().ToString("X2")
                                        + ":" + _smdReader.ReadByte().ToString("X2")
                                        )
                                    )
                                );
                    }
                    else if (cmd == 0xA9)
                    {
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    1, // text event
                                    Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":" + _smdReader.ReadByte().ToString("X2"))
                                    )
                                );
                    }
                    else if (cmd == 0xAA)
                    {
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    1, // text event
                                    Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":" + _smdReader.ReadByte().ToString("X2"))
                                    )
                                );
                    }
                    else if (cmd == 0xAC)
                    {
                        //hasEvents = true;
                        cProgram = _smdReader.ReadByte();
                        cTrack.MidiEvents.Add(
                            new MidiMessage(
                                cTick,
                                (byte)(cMidiChn & 0xF),
                                midimap[cProgram],
                                0, // dummy
                                NormalType.Program
                                )
                            );
                    }
                    else if (cmd == 0xB2)
                    {
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    1, // text event
                                    Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":" + _smdReader.ReadByte().ToString("X2"))
                                    )
                                );
                    }
                    else if (cmd == 0xB4)
                    {
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    1, // text event
                                    Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":" + _smdReader.ReadByte().ToString("X2")
                                        + ":" + _smdReader.ReadByte().ToString("X2")
                                        )
                                    )
                                );
                    }
                    else if (cmd == 0xB5)
                    {
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    1, // text event
                                    Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":" + _smdReader.ReadByte().ToString("X2"))
                                    )
                                );
                    }
                    else if (cmd == 0xBE)
                    {
                        cTrack.MidiEvents.Add(
                            new MidiMessage(
                                cTick,
                                (byte)(cMidiChn & 0xF),
                                1, // mod wheel
                                _smdReader.ReadByte(),
                                NormalType.Controller
                                )
                            );
                    }
                    else if (cmd == 0xBF)
                    {
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    1, // text event
                                    Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":" + _smdReader.ReadByte().ToString("X2"))
                                    )
                                );
                    }
                    else if (cmd == 0xC0)
                    {
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    1, // text event
                                    Encoding.ASCII.GetBytes(cmd.ToString("X2"))
                                    )
                                );
                    }
                    else if (cmd == 0xD0)
                    {
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    1, // text event
                                    Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":" + _smdReader.ReadByte().ToString("X2"))
                                    )
                                );
                    }
                    else if (cmd == 0xD1)
                    {
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    1, // text event
                                    Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":" + _smdReader.ReadByte().ToString("X2"))
                                    )
                                );
                    }
                    else if (cmd == 0xD2)
                    {
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    1, // text event
                                    Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":" + _smdReader.ReadByte().ToString("X2"))
                                    )
                                );
                    }
                    else if (cmd == 0xD4)
                    {
                        cTrack.MidiEvents.Add(
                            new MetaMessage(
                                cTick,
                                1, // text event
                                Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":"
                                    + _smdReader.ReadByte().ToString("X2") + ":"
                                    + _smdReader.ReadByte().ToString("X2") + ":"
                                    + _smdReader.ReadByte().ToString("X2")
                                    )
                                )
                            );
                    }
                    else if (cmd == 0xD6)
                    {
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    1, // text event
                                    Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":" + _smdReader.ReadByte().ToString("X2")
                                        + ":" + _smdReader.ReadByte().ToString("X2")
                                        )
                                    )
                                );
                    }
                    else if (cmd == 0xD7)
                    {
                        //hasEvents = true;
                        byte a = _smdReader.ReadByte();
                        byte b = _smdReader.ReadByte();

                        // insert bend rage if it hasn't been yet

                        if (!bendIsInit)
                        {
                            // rp msb = 0; rp lsb = 0; de msb = 8; de lsb = 0
                            cTrack.MidiEvents.Add(new MidiMessage(0, (byte)(cMidiChn & 0xF), 101, 0, NormalType.Controller));
                            cTrack.MidiEvents.Add(new MidiMessage(0, (byte)(cMidiChn & 0xF), 100, 0, NormalType.Controller));
                            cTrack.MidiEvents.Add(new MidiMessage(0, (byte)(cMidiChn & 0xF), 6, 8, NormalType.Controller));
                            cTrack.MidiEvents.Add(new MidiMessage(0, (byte)(cMidiChn & 0xF), 38, 0, NormalType.Controller));
                            bendIsInit = true;
                        }
                        ushort pitch = (ushort)(((a << 8) | b ) + 0x8000);
                        byte msb = (byte)(pitch >> 9);
                        byte lsb = (byte)((pitch >> 2) & 0x7F);
                        cTrack.MidiEvents.Add(
                            new MidiMessage(
                                cTick,
                                (byte)(cMidiChn & 0xF),
                                lsb,
                                msb,
                                NormalType.PitchBend
                                )
                            );
                        /*cTrack.MidiEvents.Add(
                            new MetaMessage(
                                cTick,
                                1,
                                Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":" + a.ToString("X2")
                                    + ":" + b.ToString("X2")
                                    )
                                )
                            );*/
                    }
                    else if (cmd == 0xDB)
                    {
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    1, // text event
                                    Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":" + _smdReader.ReadByte().ToString("X2"))
                                    )
                                );
                    }
                    else if (cmd == 0xDC)
                    {
                        cTrack.MidiEvents.Add(
                            new MetaMessage(
                                cTick,
                                1, // text event
                                Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":"
                                    + _smdReader.ReadByte().ToString("X2") + ":"
                                    + _smdReader.ReadByte().ToString("X2") + ":"
                                    + _smdReader.ReadByte().ToString("X2") + ":"
                                    + _smdReader.ReadByte().ToString("X2") + ":"
                                    + _smdReader.ReadByte().ToString("X2")
                                    )
                                )
                            );
                    }
                    else if (cmd == 0xE0)
                    {
                        //hasEvents = true;
                        cTrack.MidiEvents.Add(
                            new MidiMessage(
                                cTick,
                                (byte)(cMidiChn & 0xF),
                                7, // volume controller
                                _smdReader.ReadByte(),
                                NormalType.Controller
                                )
                            );
                    }
                    else if (cmd == 0xE2)
                    {
                        cTrack.MidiEvents.Add(
                            new MetaMessage(
                                cTick,
                                1, // text event
                                Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":"
                                    + _smdReader.ReadByte().ToString("X2") + ":"
                                    + _smdReader.ReadByte().ToString("X2") + ":"
                                    + _smdReader.ReadByte().ToString("X2")
                                    )
                                )
                            );
                    }
                    else if (cmd == 0xE3)
                    {
                        //hasEvents = true;
                        cTrack.MidiEvents.Add(
                                new MidiMessage(
                                    cTick,
                                    (byte)(cMidiChn & 0xF),
                                    11, // expression controller
                                    _smdReader.ReadByte(),
                                    NormalType.Controller
                                    )
                                );
                    }
                    else if (cmd == 0xE8)
                    {
                        //hasEvents = true;
                        cTrack.MidiEvents.Add(
                                new MidiMessage(
                                    cTick,
                                    (byte)(cMidiChn & 0xF),
                                    0xA, // PAN controller
                                    _smdReader.ReadByte(),
                                    NormalType.Controller
                                    )
                                );
                    }
                    else if (cmd == 0xEA)
                    {
                        cTrack.MidiEvents.Add(
                            new MetaMessage(
                                cTick,
                                1, // text event
                                Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":"
                                    + _smdReader.ReadByte().ToString("X2") + ":"
                                    + _smdReader.ReadByte().ToString("X2") + ":"
                                    + _smdReader.ReadByte().ToString("X2")
                                    )
                                )
                            );
                    }
                    else if (cmd == 0xF6)
                    {
                        cTrack.MidiEvents.Add(
                                new MetaMessage(
                                    cTick,
                                    1, // text event
                                    Encoding.ASCII.GetBytes(cmd.ToString("X2") + ":" + _smdReader.ReadByte().ToString("X2"))
                                    )
                                );
                    }
                    else
                    {
                        throw new Exception("Command " + cmd.ToString("X2") + " at 0x" + _smdReader.BaseStream.Position.ToString(("X8")) + " not known");
                    }
                } // end while

                cMidiChn++;
            }
            _midiFile.SortTrackEvents();
            FixMidiChannels();
        }
    }

    class ProcyonSequenceInfo
    {
        public ProcyonSequenceInfo(FileStream fs)
        {
            BinaryReader br = new BinaryReader(fs);
            // get filesize
            br.BaseStream.Position = 0x8;
            FileSize = br.ReadInt32();
            // get File Name
            br.BaseStream.Position = 0x20;
            byte[] stringBuffer = new byte[15];
            br.Read(stringBuffer, 0, 15);
            SequenceName = Encoding.ASCII.GetString(stringBuffer);
            // get the amount of tracks
            br.BaseStream.Position = 0x56;
            NumTracks = br.ReadByte();
            // generate dependent arrays

            //TrackChannels = new byte[NumTracks];
            TrackOffset = new int[NumTracks];
            TrackLength = new int[NumTracks];

            // now get all the track's information
            int currentPosition = 0x80;
            br.BaseStream.Position = currentPosition;

            for (int currentTrack = 0; currentTrack < NumTracks; currentTrack++)
            {
                // write down the track start position
                TrackOffset[currentTrack] = (int)br.BaseStream.Position;
                stringBuffer = new byte[4];
                br.Read(stringBuffer, 0, 4);
                if (Encoding.ASCII.GetString(stringBuffer) != "trk ") throw new Exception("Invalid Track identifier at 0x" + (br.BaseStream.Position - 4).ToString("X"));
                br.BaseStream.Position += 0x8;
                // store the length of the track
                TrackLength[currentTrack] = br.ReadInt32();
                //TrackChannels[currentTrack] = (byte)((br.ReadInt32() >> 8) & 0xFF);
                // jump to the next track (upround the last 2 bits
                br.BaseStream.Position += ((TrackLength[currentTrack] + 3) >> 2) << 2;
            }
        }

        public string SequenceName;

        public int FileSize;
        public int NumTracks;
        public int[] TrackOffset;
        public int[] TrackLength;
        //public byte[] TrackChannels;
    }
}
