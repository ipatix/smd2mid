using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace FileMIDI
{
    static class MidiSaver
    {
        public static void SaveToFile(string filePath, ref List<MidiTrack> midiTracks, ushort timeDivision)
        {
            Console.WriteLine("Saving MIDI to type 1 file...");
            // first of all check if a file with the name already exists
            if (File.Exists(filePath)) File.Delete(filePath);
            FileStream midiFileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None);
            BinaryWriter midiWriter = new BinaryWriter(midiFileStream);
            Console.WriteLine("The new MIDI file has {0} tracks!", midiTracks.Count);

            // first of all write MIDI header string
            midiWriter.Write(Encoding.ASCII.GetBytes("MThd"));
            // writer the header chunk length (=6)
            midiWriter.Write(MidiLoader.IntToBigEndian(6));
            // write the midi file type (=1)
            midiWriter.Write(MidiLoader.UshortToBigEndian(1));
            // write the amount of tracks
            midiWriter.Write(MidiLoader.UshortToBigEndian((ushort)midiTracks.Count));
            // write the time division
            midiWriter.Write(MidiLoader.UshortToBigEndian(timeDivision));
            // finished writing the header, now do the tracks
            long[] trackHeaderOffset = new long[midiTracks.Count];
            long[] trackStartOffset = new long[midiTracks.Count];
            long[] trackEndOffset = new long[midiTracks.Count];

            for (int currentTrack = 0; currentTrack < midiTracks.Count; currentTrack++)
            {
                trackHeaderOffset[currentTrack] = midiWriter.BaseStream.Position;    // save the offset to the track header
                // write the header info
                midiWriter.Write(Encoding.ASCII.GetBytes("MTrk"));
                midiWriter.Write((int)0);           // write 0 into the chunk length slot; it'll get filled later; 0 is the same in Little Endian as in Big, so we can use the normal int32 writing

                trackStartOffset[currentTrack] = midiWriter.BaseStream.Position;     // save the track beginning (doesn't point to the header, it points to the data)
                long currentTick = 0;   // init the current tick to 0 to calculate Delta Time values

                for (int currentEvent = 0; currentEvent < midiTracks[currentTrack].MidiEvents.Count; currentEvent++)
                {
                    // write the Delta time to the stream
                    midiWriter.Write(VariableLength.ConvertToVariableLength(midiTracks[currentTrack].MidiEvents[currentEvent].GetAbsoluteTicks() - currentTick));
                    // write the actual Event data
                    midiWriter.Write(midiTracks[currentTrack].MidiEvents[currentEvent].GetEventData());

                    currentTick = midiTracks[currentTrack].MidiEvents[currentEvent].GetAbsoluteTicks();
                }

                midiWriter.Write((byte)0x0);    // write delta time for track end
                midiWriter.Write((byte)0xFF);   // write META event byte
                midiWriter.Write((byte)0x2F);   // write End of Track byte
                midiWriter.Write((byte)0x0);    // the length of this META event data is 0 (no data follows)

                trackEndOffset[currentTrack] = midiWriter.BaseStream.Position;   // calc the length of the event data and backup the position
            }   // end of track loop
            midiWriter.BaseStream.Close();
            // close the filestreams and create a new one to edit the file
            midiFileStream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None);
            midiWriter = new BinaryWriter(midiFileStream);

            for (int currentTrack = 0; currentTrack < midiTracks.Count; currentTrack++)
            {
                midiWriter.BaseStream.Position = trackHeaderOffset[currentTrack] + 4;
                midiWriter.Write(MidiLoader.IntToBigEndian((int)(trackEndOffset[currentTrack] - trackStartOffset[currentTrack])));
            }
            midiWriter.Close();
            // close filestream and finish
            Console.WriteLine("Successfully finished creating MIDI file!");
        }   // end of function
    }
}
