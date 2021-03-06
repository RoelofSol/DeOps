﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace DeOps.Services.Voice
{
    class Speex
    {

        [StructLayout(LayoutKind.Sequential)]
        public struct SpeexBits
        {
            public IntPtr chars;    /**< "raw" data */
            public int nbBits;      /**< Total number of bits stored in the stream*/
            public int charPtr;     /**< Position of the byte "cursor" */
            public int bitPtr;      /**< Position of the bit "cursor" within the current char */
            public int owner;       /**< Does the struct "own" the "raw" buffer (member "chars") */
            public int overflow;    /**< Set to one if we try to read past the valid data */
            public int buf_size;    /**< Allocated size for buffer */
            public int reserved1;   /**< Reserved for future use */
            public IntPtr reserved2;/**< Reserved for future use */
        }



        /** Encoder state initialization function */
        // typedef void *(*encoder_init_func)(const struct SpeexMode *mode);
        delegate void encoder_init_func(SpeexMode mode);

        /** Encoder state destruction function */
        //typedef void (*encoder_destroy_func)(void *st);
        delegate void encoder_destroy_func(IntPtr st);

        /** Main encoding function */
        //typedef int (*encode_func)(void *state, void *in, SpeexBits *bits);
        delegate int encode_func(IntPtr state, IntPtr inx, SpeexBits bits);

        /** Function for controlling the encoder options */
        //typedef int (*encoder_ctl_func)(void *state, int request, void *ptr);
        delegate int encoder_ctl_func(IntPtr state, int request, IntPtr ptr);

        /** Decoder state initialization function */
        //typedef void *(*decoder_init_func)(const struct SpeexMode *mode);
        delegate void decoder_init_func(SpeexMode mode);

        /** Decoder state destruction function */
        //typedef void (*decoder_destroy_func)(void *st);
        delegate void decoder_destroy_func(IntPtr st);

        /** Main decoding function */
        //typedef int  (*decode_func)(void *state, SpeexBits *bits, void *out);
        delegate int decode_func(IntPtr state, SpeexBits bits, IntPtr outx);

        /** Function for controlling the decoder options */
        //typedef int (*decoder_ctl_func)(void *state, int request, void *ptr);
        delegate int decoder_ctl_func(IntPtr state, int request, IntPtr ptr);

        /** Query function for a mode */
        //typedef int (*mode_query_func)(const void *mode, int request, void *ptr);
        delegate int mode_query_func(IntPtr mode, int request, IntPtr ptr);


        [StructLayout(LayoutKind.Sequential)]
        public struct SpeexMode
        {
            /** Pointer to the low-level mode data */
            IntPtr mode;

            /** Pointer to the mode query function */
            mode_query_func query;

            /** The name of the mode (you should not rely on this to identify the mode)*/
            IntPtr modeName;

            /**ID of the mode*/
            int modeID;

            /**Version number of the bitstream (incremented every time we break
             bitstream compatibility*/
            int bitstream_version;

            /** Pointer to encoder initialization function */
            encoder_init_func enc_init;

            /** Pointer to encoder destruction function */
            encoder_destroy_func enc_destroy;

            /** Pointer to frame encoding function */
            encode_func enc;

            /** Pointer to decoder initialization function */
            decoder_init_func dec_init;

            /** Pointer to decoder destruction function */
            decoder_destroy_func dec_destroy;

            /** Pointer to frame decoding function */
            decode_func dec;

            /** ioctl-like requests for encoder */
            encoder_ctl_func enc_ctl;

            /** ioctl-like requests for decoder */
            decoder_ctl_func dec_ctl;
        }


        /* Values allowed for *ctl() requests */

        /** Set enhancement on/off (decoder only) */
        public static int SPEEX_SET_ENH = 0;
        /** Get enhancement state (decoder only) */
        public static int SPEEX_GET_ENH = 1;

        /*Would be SPEEX_SET_FRAME_SIZE, but it's (currently) invalid*/
        /** Obtain frame size used by encoder/decoder */
        public static int SPEEX_GET_FRAME_SIZE = 3;

        /** Set quality value */
        public static int SPEEX_SET_QUALITY = 4;
        /** Get current quality setting */
        /* int SPEEX_GET_QUALITY 5 -- Doesn't make much sense, does it? */

        /** Set sub-mode to use */
        public static int SPEEX_SET_MODE = 6;
        /** Get current sub-mode in use */
        public static int SPEEX_GET_MODE = 7;

        /** Set low-band sub-mode to use (wideband only)*/
        public static int SPEEX_SET_LOW_MODE = 8;
        /** Get current low-band mode in use (wideband only)*/
        public static int SPEEX_GET_LOW_MODE = 9;

        /** Set high-band sub-mode to use (wideband only)*/
        public static int SPEEX_SET_HIGH_MODE = 10;
        /** Get current high-band mode in use (wideband only)*/
        public static int SPEEX_GET_HIGH_MODE = 11;

        /** Set VBR on (1) or off (0) */
        public static int SPEEX_SET_VBR = 12;
        /** Get VBR status (1 for on, 0 for off) */
        public static int SPEEX_GET_VBR = 13;

        /** Set quality value for VBR encoding (0-10) */
        public static int SPEEX_SET_VBR_QUALITY = 14;
        /** Get current quality value for VBR encoding (0-10) */
        public static int SPEEX_GET_VBR_QUALITY = 15;

        /** Set complexity of the encoder (0-10) */
        public static int SPEEX_SET_COMPLEXITY = 16;
        /** Get current complexity of the encoder (0-10) */
        public static int SPEEX_GET_COMPLEXITY = 17;

        /** Set bit-rate used by the encoder (or lower) */
        public static int SPEEX_SET_BITRATE = 18;
        /** Get current bit-rate used by the encoder or decoder */
        public static int SPEEX_GET_BITRATE = 19;

        /** Define a handler function for in-band Speex request*/
        public static int SPEEX_SET_HANDLER = 20;

        /** Define a handler function for in-band user-defined request*/
        public static int SPEEX_SET_USER_HANDLER = 22;

        /** Set sampling rate used in bit-rate computation */
        public static int SPEEX_SET_SAMPLING_RATE = 24;
        /** Get sampling rate used in bit-rate computation */
        public static int SPEEX_GET_SAMPLING_RATE = 25;

        /** Reset the encoder/decoder memories to zero*/
        public static int SPEEX_RESET_STATE = 26;

        /** Get VBR info (mostly used internally) */
        public static int SPEEX_GET_RELATIVE_QUALITY = 29;

        /** Set VAD status (1 for on, 0 for off) */
        public static int SPEEX_SET_VAD = 30;

        /** Get VAD status (1 for on, 0 for off) */
        public static int SPEEX_GET_VAD = 31;

        /** Set Average Bit-Rate (ABR) to n bits per seconds */
        public static int SPEEX_SET_ABR = 32;
        /** Get Average Bit-Rate (ABR) setting (in bps) */
        public static int SPEEX_GET_ABR = 33;

        /** Set DTX status (1 for on, 0 for off) */
        public static int SPEEX_SET_DTX = 34;
        /** Get DTX status (1 for on, 0 for off) */
        public static int SPEEX_GET_DTX = 35;

        /** Set submode encoding in each frame (1 for yes, 0 for no, setting to no breaks the standard) */
        public static int SPEEX_SET_SUBMODE_ENCODING = 36;
        /** Get submode encoding in each frame */
        public static int SPEEX_GET_SUBMODE_ENCODING = 37;

        /*int SPEEX_SET_LOOKAHEAD 38*/
        /** Returns the lookahead used by Speex */
        public static int SPEEX_GET_LOOKAHEAD = 39;

        /** Sets tuning for packet-loss concealment (expected loss rate) */
        public static int SPEEX_SET_PLC_TUNING = 40;
        /** Gets tuning for PLC */
        public static int SPEEX_GET_PLC_TUNING = 41;

        /** Sets the max bit-rate allowed in VBR mode */
        public static int SPEEX_SET_VBR_MAX_BITRATE = 42;
        /** Gets the max bit-rate allowed in VBR mode */
        public static int SPEEX_GET_VBR_MAX_BITRATE = 43;

        /** Turn on/off input/output high-pass filtering */
        public static int SPEEX_SET_HIGHPASS = 44;
        /** Get status of input/output high-pass filtering */
        public static int SPEEX_GET_HIGHPASS = 45;

        /** Get "activity level" of the last decoded frame, i.e.
            how much damage we cause if we remove the frame */
        public static int SPEEX_GET_ACTIVITY = 47;


        /* Preserving compatibility:*/
        /** Equivalent to SPEEX_SET_ENH */
        public static int SPEEX_SET_PF = 0;
        /** Equivalent to SPEEX_GET_ENH */
        public static int SPEEX_GET_PF = 1;


        /* Values allowed for mode queries */
        /** Query the frame size of a mode */
        public static int SPEEX_MODE_FRAME_SIZE = 0;

        /** Query the size of an encoded frame for a particular sub-mode */
        public static int SPEEX_SUBMODE_BITS_PER_FRAME = 1;


        /** Get major Speex version */
        public static int SPEEX_LIB_GET_MAJOR_VERSION = 1;
        /** Get minor Speex version */
        public static int SPEEX_LIB_GET_MINOR_VERSION = 3;
        /** Get micro Speex version */
        public static int SPEEX_LIB_GET_MICRO_VERSION = 5;
        /** Get extra Speex version */
        public static int SPEEX_LIB_GET_EXTRA_VERSION = 7;
        /** Get Speex version string */
        public static int SPEEX_LIB_GET_VERSION_STRING = 9;

        /** Initializes and allocates resources for a SpeexBits struct */
        [DllImport("libspeex.dll")]
        //void speex_bits_init(SpeexBits *bits);
        public static extern void speex_bits_init(ref SpeexBits bits);

        /** Initializes SpeexBits struct using a pre-allocated buffer*/
        [DllImport("libspeex.dll")]
        //void speex_bits_init_buffer(SpeexBits *bits, void *buff, int buf_size);
        public static extern void speex_bits_init_buffer(ref SpeexBits bits, IntPtr buff, int buf_size);

        /** Sets the bits in a SpeexBits struct to use data from an existing buffer (for decoding without copying data) */
        [DllImport("libspeex.dll")]
        //void speex_bits_set_bit_buffer(SpeexBits *bits, void *buff, int buf_size);
        public static extern void speex_bits_set_bit_buffer(ref SpeexBits bits, IntPtr buff, int buf_size);

        /** Frees all resources associated to a SpeexBits struct. Right now this does nothing since no resources are allocated, but this could change in the future.*/
        [DllImport("libspeex.dll")]
        //void speex_bits_destroy(SpeexBits *bits);
        public static extern void speex_bits_destroy(ref SpeexBits bits);

        /** Resets bits to initial value (just after initialization, erasing content)*/
        [DllImport("libspeex.dll")]
        //void speex_bits_reset(SpeexBits *bits);
        public static extern void speex_bits_reset(ref SpeexBits bits);

        /** Rewind the bit-stream to the beginning (ready for read) without erasing the content */
        [DllImport("libspeex.dll")]
        //void speex_bits_rewind(SpeexBits *bits);
        public static extern void speex_bits_rewind(ref SpeexBits bits);

        /** Initializes the bit-stream from the data in an area of memory */
        [DllImport("libspeex.dll")]
        //void speex_bits_read_from(SpeexBits *bits, char *bytes, int len);
        public static extern void speex_bits_read_from(ref SpeexBits bits, byte[] bytes, int len);

        /** Append bytes to the bit-stream
         * 
         * @param bits Bit-stream to operate on
         * @param bytes pointer to the bytes what will be appended
         * @param len Number of bytes of append
         */
        [DllImport("libspeex.dll")]
        //void speex_bits_read_whole_bytes(SpeexBits *bits, char *bytes, int len);
        public static extern void speex_bits_read_whole_bytes(ref SpeexBits bits, IntPtr bytes, int len);

        /** Write the content of a bit-stream to an area of memory
         * 
         * @param bits Bit-stream to operate on
         * @param bytes Memory location where to write the bits
         * @param max_len Maximum number of bytes to write (i.e. size of the "bytes" buffer)
         * @return Number of bytes written to the "bytes" buffer
        */
        [DllImport("libspeex.dll")]
        //int speex_bits_write(SpeexBits *bits, char *bytes, int max_len);
        public static extern int speex_bits_write(ref SpeexBits bits, byte[] bytes, int max_len);

        /** Like speex_bits_write, but writes only the complete bytes in the stream. Also removes the written bytes from the stream */
        [DllImport("libspeex.dll")]
        //int speex_bits_write_whole_bytes(SpeexBits *bits, char *bytes, int max_len);
        public static extern int speex_bits_write_whole_bytes(ref SpeexBits bits, IntPtr bytes, int max_len);

        /** Append bits to the bit-stream
         * @param bits Bit-stream to operate on
         * @param data Value to append as integer
         * @param nbBits number of bits to consider in "data"
         */
        [DllImport("libspeex.dll")]
        //void speex_bits_pack(SpeexBits *bits, int data, int nbBits);
        public static extern void speex_bits_pack(ref SpeexBits bits, int data, int nbBits);

        /** Interpret the next bits in the bit-stream as a signed integer
         *
         * @param bits Bit-stream to operate on
         * @param nbBits Number of bits to interpret
         * @return A signed integer represented by the bits read
         */
        [DllImport("libspeex.dll")]
        //int speex_bits_unpack_signed(SpeexBits *bits, int nbBits);
        public static extern int speex_bits_unpack_signed(ref SpeexBits bits, int nbBits);

        /** Interpret the next bits in the bit-stream as an unsigned integer
         *
         * @param bits Bit-stream to operate on
         * @param nbBits Number of bits to interpret
         * @return An unsigned integer represented by the bits read
         */
        [DllImport("libspeex.dll")]
        //unsigned int speex_bits_unpack_unsigned(SpeexBits *bits, int nbBits);
        public static extern uint speex_bits_unpack_unsigned(ref SpeexBits bits, int nbBits);

        /** Returns the number of bytes in the bit-stream, including the last one even if it is not "full"
         *
         * @param bits Bit-stream to operate on
         * @return Number of bytes in the stream
         */
        [DllImport("libspeex.dll")]
        //int speex_bits_nbytes(SpeexBits *bits);
        public static extern int speex_bits_nbytes(ref SpeexBits bits);

        /** Same as speex_bits_unpack_unsigned, but without modifying the cursor position 
         * 
         * @param bits Bit-stream to operate on
         * @param nbBits Number of bits to look for
         * @return Value of the bits peeked, interpreted as unsigned
         */
        [DllImport("libspeex.dll")]
        //unsigned int speex_bits_peek_unsigned(SpeexBits *bits, int nbBits);
        public static extern uint speex_bits_peek_unsigned(ref SpeexBits bits, int nbBits);

        /** Get the value of the next bit in the stream, without modifying the
         * "cursor" position 
         * 
         * @param bits Bit-stream to operate on
         * @return Value of the bit peeked (one bit only)
         */
        [DllImport("libspeex.dll")]
        //int speex_bits_peek(SpeexBits *bits);
        public static extern int speex_bits_peek(ref SpeexBits bits);

        /** Advances the position of the "bit cursor" in the stream 
         *
         * @param bits Bit-stream to operate on
         * @param n Number of bits to advance
         */
        [DllImport("libspeex.dll")]
        //void speex_bits_advance(SpeexBits *bits, int n);
        public static extern void speex_bits_advance(ref SpeexBits bits, int n);

        /** Returns the number of bits remaining to be read in a stream
         *
         * @param bits Bit-stream to operate on
         * @return Number of bits that can still be read from the stream
         */
        [DllImport("libspeex.dll")]
        //int speex_bits_remaining(SpeexBits *bits);
        public static extern int speex_bits_remaining(ref SpeexBits bits);

        /** Insert a terminator so that the data can be sent as a packet while auto-detecting 
         * the number of frames in each packet 
         *
         * @param bits Bit-stream to operate on
         */
        [DllImport("libspeex.dll")]
        //void speex_bits_insert_terminator(SpeexBits *bits);
        public static extern void speex_bits_insert_terminator(ref SpeexBits bits);



        /**
        * Returns a handle to a newly created Speex encoder state structure. For now, 
        * the "mode" argument can be &nb_mode or &wb_mode . In the future, more modes 
        * may be added. Note that for now if you have more than one channels to 
        * encode, you need one state per channel.
        *
        * @param mode The mode to use (either speex_nb_mode or speex_wb.mode) 
        * @return A newly created encoder state or NULL if state allocation fails
        */
        [DllImport("libspeex.dll")]
        //void *speex_encoder_init(const SpeexMode *mode);
        public static extern IntPtr speex_encoder_init(IntPtr mode);

        /** Frees all resources associated to an existing Speex encoder state. 
         * @param state Encoder state to be destroyed */
        [DllImport("libspeex.dll")]
        //void speex_encoder_destroy(void *state);
        public static extern void speex_encoder_destroy(IntPtr state);

        /** Uses an existing encoder state to encode one frame of speech pointed to by
            "in". The encoded bit-stream is saved in "bits".
         @param state Encoder state
         @param in Frame that will be encoded with a +-2^15 range. This data MAY be 
                overwritten by the encoder and should be considered uninitialised 
                after the call.
         @param bits Bit-stream where the data will be written
         @return 0 if frame needs not be transmitted (DTX only), 1 otherwise
         */
        [DllImport("libspeex.dll")]
        //int speex_encode(void *state, float *in, SpeexBits *bits);
        public static extern int speex_encode(IntPtr state, IntPtr inFloat, ref SpeexBits bits);

        /** Uses an existing encoder state to encode one frame of speech pointed to by
            "in". The encoded bit-stream is saved in "bits".
         @param state Encoder state
         @param in Frame that will be encoded with a +-2^15 range
         @param bits Bit-stream where the data will be written
         @return 0 if frame needs not be transmitted (DTX only), 1 otherwise
         */
        [DllImport("libspeex.dll")]
        //int speex_encode_int(void *state, spx_int16_t *in, SpeexBits *bits);
        public static extern int speex_encode_int(IntPtr state, IntPtr inShort, ref SpeexBits bits);

        /** Used like the ioctl function to control the encoder parameters
         *
         * @param state Encoder state
         * @param request ioctl-type request (one of the SPEEX_* macros)
         * @param ptr Data exchanged to-from function
         * @return 0 if no error, -1 if request in unknown, -2 for invalid parameter
         */
        [DllImport("libspeex.dll")]
        //int speex_encoder_ctl(void *state, int request, void *ptr);
        public static extern int speex_encoder_ctl(IntPtr state, int request, ref int value);

        /** Returns a handle to a newly created decoder state structure. For now, 
         * the mode argument can be &nb_mode or &wb_mode . In the future, more modes
         * may be added.  Note that for now if you have more than one channels to
         * decode, you need one state per channel.
         *
         * @param mode Speex mode (one of speex_nb_mode or speex_wb_mode)
         * @return A newly created decoder state or NULL if state allocation fails
         */
        [DllImport("libspeex.dll")]
        //void *speex_decoder_init(const SpeexMode *mode);
        public static extern IntPtr speex_decoder_init(IntPtr mode);

        /** Frees all resources associated to an existing decoder state.
         *
         * @param state State to be destroyed
         */
        [DllImport("libspeex.dll")]
        //void speex_decoder_destroy(void *state);
        public static extern void speex_decoder_destroy(IntPtr state);
        /** Uses an existing decoder state to decode one frame of speech from
         * bit-stream bits. The output speech is saved written to out.
         *
         * @param state Decoder state
         * @param bits Bit-stream from which to decode the frame (NULL if the packet was lost)
         * @param out Where to write the decoded frame
         * @return return status (0 for no error, -1 for end of stream, -2 corrupt stream)
         */
        [DllImport("libspeex.dll")]
        //int speex_decode(void *state, SpeexBits *bits, float *out);
        public static extern int speex_decode(IntPtr state, ref SpeexBits bits, IntPtr outFloat);

        /** Uses an existing decoder state to decode one frame of speech from
         * bit-stream bits. The output speech is saved written to out.
         *
         * @param state Decoder state
         * @param bits Bit-stream from which to decode the frame (NULL if the packet was lost)
         * @param out Where to write the decoded frame
         * @return return status (0 for no error, -1 for end of stream, -2 corrupt stream)
         */
        [DllImport("libspeex.dll")]
        //int speex_decode_int(void *state, SpeexBits *bits, spx_int16_t *out);
        public static extern int speex_decode_int(IntPtr state, ref SpeexBits bits, byte[] outShort);

        /** Used like the ioctl function to control the encoder parameters
         *
         * @param state Decoder state
         * @param request ioctl-type request (one of the SPEEX_* macros)
         * @param ptr Data exchanged to-from function
         * @return 0 if no error, -1 if request in unknown, -2 for invalid parameter
         */
        [DllImport("libspeex.dll")]
        //int speex_decoder_ctl(void *state, int request, void *ptr);
        public static extern int speex_decoder_ctl(IntPtr state, int request, ref int ptr);

        /** Query function for mode information
         *
         * @param mode Speex mode
         * @param request ioctl-type request (one of the SPEEX_* macros)
         * @param ptr Data exchanged to-from function
         * @return 0 if no error, -1 if request in unknown, -2 for invalid parameter
         */
        [DllImport("libspeex.dll")]
        //int speex_mode_query(const SpeexMode *mode, int request, void *ptr);
        public static extern int speex_mode_query(ref SpeexMode mode, int request, IntPtr ptr);

        /** Functions for controlling the behavior of libspeex
         * @param request ioctl-type request (one of the SPEEX_LIB_* macros)
         * @param ptr Data exchanged to-from function
         * @return 0 if no error, -1 if request in unknown, -2 for invalid parameter
         */
        [DllImport("libspeex.dll")]
        //int speex_lib_ctl(int request, void *ptr);
        public static extern int speex_lib_ctl(int request, IntPtr ptr);

        public static int SPEEX_MODEID_NB = 0;

        /** modeID for the defined wideband mode */
        public static int SPEEX_MODEID_WB = 1;

        /** modeID for the defined ultra-wideband mode */
        public static int SPEEX_MODEID_UWB = 2;

        [DllImport("libspeex.dll")]
        //const SpeexMode * speex_lib_get_mode (int mode);
        public static extern IntPtr speex_lib_get_mode(int mode);




        /** Creates a new preprocessing state. You MUST create one state per channel processed.
         * @param frame_size Number of samples to process at one time (should correspond to 10-20 ms). Must be
         * the same value as that used for the echo canceller for residual echo cancellation to work.
         * @param sampling_rate Sampling rate used for the input.
         * @return Newly created preprocessor state
        */
        [DllImport("libspeex.dll")]
        //SpeexPreprocessState *speex_preprocess_state_init(int frame_size, int sampling_rate);
        public static extern IntPtr speex_preprocess_state_init(int frame_size, int sampling_rate);

        /** Destroys a preprocessor state 
         * @param st Preprocessor state to destroy
        */
        [DllImport("libspeex.dll")]
        //void speex_preprocess_state_destroy(SpeexPreprocessState *st);
        public static extern void speex_preprocess_state_destroy(IntPtr st);

        /** Preprocess a frame 
         * @param st Preprocessor state
         * @param x Audio sample vector (in and out). Must be same size as specified in speex_preprocess_state_init().
         * @return Bool value for voice activity (1 for speech, 0 for noise/silence), ONLY if VAD turned on.
        */
        [DllImport("libspeex.dll")]
        //int speex_preprocess_run(SpeexPreprocessState *st, spx_int16_t *x);
        public static extern int speex_preprocess_run(IntPtr st, byte[] x);

        /** Preprocess a frame (deprecated, use speex_preprocess_run() instead)*/
        [DllImport("libspeex.dll")]
        //int speex_preprocess(SpeexPreprocessState *st, spx_int16_t *x, spx_int32_t *echo);
        public static extern int speex_preprocess(IntPtr st, byte[] x, byte[] echo);

        /** Update preprocessor state, but do not compute the output
         * @param st Preprocessor state
         * @param x Audio sample vector (in only). Must be same size as specified in speex_preprocess_state_init().
        */
        [DllImport("libspeex.dll")]
        //void speex_preprocess_estimate_update(SpeexPreprocessState *st, spx_int16_t *x);
        public static extern void speex_preprocess_estimate_update(IntPtr st, byte[] x);

        /** Used like the ioctl function to control the preprocessor parameters 
         * @param st Preprocessor state
         * @param request ioctl-type request (one of the SPEEX_PREPROCESS_* macros)
         * @param ptr Data exchanged to-from function
         * @return 0 if no error, -1 if request in unknown
        */
        [DllImport("libspeex.dll")]
        //int speex_preprocess_ctl(SpeexPreprocessState *st, int request, void *ptr);
        public static extern int speex_preprocess_ctl(IntPtr st, int request, ref int ptr);

        [DllImport("libspeex.dll")]
        public static extern int speex_preprocess_ctl(IntPtr st, int request, IntPtr ptr);

        /** Set preprocessor denoiser state */
        public static int SPEEX_PREPROCESS_SET_DENOISE = 0;
        /** Get preprocessor denoiser state */
        public static int SPEEX_PREPROCESS_GET_DENOISE = 1;

        /** Set preprocessor Automatic Gain Control state */
        public static int SPEEX_PREPROCESS_SET_AGC = 2;
        /** Get preprocessor Automatic Gain Control state */
        public static int SPEEX_PREPROCESS_GET_AGC = 3;

        /** Set preprocessor Voice Activity Detection state */
        public static int SPEEX_PREPROCESS_SET_VAD = 4;
        /** Get preprocessor Voice Activity Detection state */
        public static int SPEEX_PREPROCESS_GET_VAD = 5;

        /** Set preprocessor Automatic Gain Control level (float) */
        public static int SPEEX_PREPROCESS_SET_AGC_LEVEL = 6;
        /** Get preprocessor Automatic Gain Control level (float) */
        public static int SPEEX_PREPROCESS_GET_AGC_LEVEL = 7;

        /** Set preprocessor dereverb state */
        public static int SPEEX_PREPROCESS_SET_DEREVERB = 8;
        /** Get preprocessor dereverb state */
        public static int SPEEX_PREPROCESS_GET_DEREVERB = 9;

        /** Set preprocessor dereverb level */
        public static int SPEEX_PREPROCESS_SET_DEREVERB_LEVEL = 10;
        /** Get preprocessor dereverb level */
        public static int SPEEX_PREPROCESS_GET_DEREVERB_LEVEL = 11;

        /** Set preprocessor dereverb decay */
        public static int SPEEX_PREPROCESS_SET_DEREVERB_DECAY = 12;
        /** Get preprocessor dereverb decay */
        public static int SPEEX_PREPROCESS_GET_DEREVERB_DECAY = 13;

        /** Set probability required for the VAD to go from silence to voice */
        public static int SPEEX_PREPROCESS_SET_PROB_START = 14;
        /** Get probability required for the VAD to go from silence to voice */
        public static int SPEEX_PREPROCESS_GET_PROB_START = 15;

        /** Set probability required for the VAD to stay in the voice state (integer percent) */
        public static int SPEEX_PREPROCESS_SET_PROB_CONTINUE = 16;
        /** Get probability required for the VAD to stay in the voice state (integer percent) */
        public static int SPEEX_PREPROCESS_GET_PROB_CONTINUE = 17;

        /** Set maximum attenuation of the noise in dB (negative number) */
        public static int SPEEX_PREPROCESS_SET_NOISE_SUPPRESS = 18;
        /** Get maximum attenuation of the noise in dB (negative number) */
        public static int SPEEX_PREPROCESS_GET_NOISE_SUPPRESS = 19;

        /** Set maximum attenuation of the residual echo in dB (negative number) */
        public static int SPEEX_PREPROCESS_SET_ECHO_SUPPRESS = 20;
        /** Get maximum attenuation of the residual echo in dB (negative number) */
        public static int SPEEX_PREPROCESS_GET_ECHO_SUPPRESS = 21;

        /** Set maximum attenuation of the residual echo in dB when near end is active (negative number) */
        public static int SPEEX_PREPROCESS_SET_ECHO_SUPPRESS_ACTIVE = 22;
        /** Get maximum attenuation of the residual echo in dB when near end is active (negative number) */
        public static int SPEEX_PREPROCESS_GET_ECHO_SUPPRESS_ACTIVE = 23;

        /** Set the corresponding echo canceller state so that residual echo suppression can be performed (NULL for no residual echo suppression) */
        public static int SPEEX_PREPROCESS_SET_ECHO_STATE = 24;
        /** Get the corresponding echo canceller state */
        public static int SPEEX_PREPROCESS_GET_ECHO_STATE = 25;

        /** Set maximal gain increase in dB/second (int32) */
        public static int SPEEX_PREPROCESS_SET_AGC_INCREMENT = 26;

        /** Get maximal gain increase in dB/second (int32) */
        public static int SPEEX_PREPROCESS_GET_AGC_INCREMENT = 27;

        /** Set maximal gain decrease in dB/second (int32) */
        public static int SPEEX_PREPROCESS_SET_AGC_DECREMENT = 28;

        /** Get maximal gain decrease in dB/second (int32) */
        public static int SPEEX_PREPROCESS_GET_AGC_DECREMENT = 29;

        /** Set maximal gain in dB (int32) */
        public static int SPEEX_PREPROCESS_SET_AGC_MAX_GAIN = 30;

        /** Get maximal gain in dB (int32) */
        public static int SPEEX_PREPROCESS_GET_AGC_MAX_GAIN = 31;

        /*  Can't set loudness */
        /** Get loudness */
        public static int SPEEX_PREPROCESS_GET_AGC_LOUDNESS = 33;

        /*  Can't set gain */
        /** Get current gain (int32 percent) */
        public static int SPEEX_PREPROCESS_GET_AGC_GAIN = 35;

        /*  Can't set spectrum size */
        /** Get spectrum size for power spectrum (int32) */
        public static int SPEEX_PREPROCESS_GET_PSD_SIZE = 37;

        /*  Can't set power spectrum */
        /** Get power spectrum (int32[] of squared values) */
        public static int SPEEX_PREPROCESS_GET_PSD = 39;

        /*  Can't set noise size */
        /** Get spectrum size for noise estimate (int32)  */
        public static int SPEEX_PREPROCESS_GET_NOISE_PSD_SIZE = 41;

        /*  Can't set noise estimate */
        /** Get noise estimate (int32[] of squared values) */
        public static int SPEEX_PREPROCESS_GET_NOISE_PSD = 43;

        /* Can't set speech probability */
        /** Get speech probability in last frame (int32).  */
        public static int SPEEX_PREPROCESS_GET_PROB = 45;

        /** Set preprocessor Automatic Gain Control level (int32) */
        public static int SPEEX_PREPROCESS_SET_AGC_TARGET = 46;
        /** Get preprocessor Automatic Gain Control level (int32) */
        public static int SPEEX_PREPROCESS_GET_AGC_TARGET = 47;




        /** Obtain frame size used by the AEC */
        public static int SPEEX_ECHO_GET_FRAME_SIZE = 3;

        /** Set sampling rate */
        public static int SPEEX_ECHO_SET_SAMPLING_RATE = 24;
        /** Get sampling rate */
        public static int SPEEX_ECHO_GET_SAMPLING_RATE = 25;

        /* Can't set window sizes */
        /** Get size of impulse response (int32) */
        public static int SPEEX_ECHO_GET_IMPULSE_RESPONSE_SIZE = 27;

        /* Can't set window content */
        /** Get impulse response (int32[]) */
        public static int SPEEX_ECHO_GET_IMPULSE_RESPONSE = 29;

        /** public echo canceller state. Should never be accessed directly. */
        //struct SpeexEchoState_;

        /** @class SpeexEchoState
         * This holds the state of the echo canceller. You need one per channel. 
        */

        /** public echo canceller state. Should never be accessed directly. */
        //typedef struct SpeexEchoState_ SpeexEchoState;

        /** Creates a new echo canceller state
         * @param frame_size Number of samples to process at one time (should correspond to 10-20 ms)
         * @param filter_length Number of samples of echo to cancel (should generally correspond to 100-500 ms)
         * @return Newly-created echo canceller state
         */
        [DllImport("libspeex.dll")]
        //SpeexEchoState *speex_echo_state_init(int frame_size, int filter_length);
        public static extern IntPtr speex_echo_state_init(int frame_size, int filter_length);

        /** Creates a new multi-channel echo canceller state
         * @param frame_size Number of samples to process at one time (should correspond to 10-20 ms)
         * @param filter_length Number of samples of echo to cancel (should generally correspond to 100-500 ms)
         * @param nb_mic Number of microphone channels
         * @param nb_speakers Number of speaker channels
         * @return Newly-created echo canceller state
         */
        [DllImport("libspeex.dll")]
        //SpeexEchoState *speex_echo_state_init_mc(int frame_size, int filter_length, int nb_mic, int nb_speakers);
        public static extern IntPtr speex_echo_state_init_mc(int frame_size, int filter_length, int nb_mic, int nb_speakers);

        /** Destroys an echo canceller state 
         * @param st Echo canceller state
        */
        [DllImport("libspeex.dll")]
        //void speex_echo_state_destroy(SpeexEchoState *st);
        public static extern void speex_echo_state_destroy(IntPtr st);

        /** Performs echo cancellation a frame, based on the audio sent to the speaker (no delay is added
         * to playback in this form)
         *
         * @param st Echo canceller state
         * @param rec Signal from the microphone (near end + far end echo)
         * @param play Signal played to the speaker (received from far end)
         * @param out Returns near-end signal with echo removed
         */
        [DllImport("libspeex.dll")]
        //void speex_echo_cancellation(SpeexEchoState *st, const spx_int16_t *rec, const spx_int16_t *play, spx_int16_t *out);
        public static extern void speex_echo_cancellation(IntPtr st, byte[] rec, byte[] play, byte[] outbuffer);

        /** Performs echo cancellation a frame (deprecated) */
        [DllImport("libspeex.dll")]
        //void speex_echo_cancel(SpeexEchoState *st, const spx_int16_t *rec, const spx_int16_t *play, spx_int16_t *out, spx_int32_t *Yout);
        public static extern void speex_echo_cancel(IntPtr st, byte[] rec, byte[] play, byte[] outbuffer, byte[] Yout);

        /** Perform echo cancellation using public playback buffer, which is delayed by two frames
         * to account for the delay introduced by most soundcards (but it could be off!)
         * @param st Echo canceller state
         * @param rec Signal from the microphone (near end + far end echo)
         * @param out Returns near-end signal with echo removed
        */
        [DllImport("libspeex.dll")]
        //void speex_echo_capture(SpeexEchoState *st, const spx_int16_t *rec, spx_int16_t *out);
        public static extern void speex_echo_capture(IntPtr st, byte[] rec, byte[] outbuffer);

        /** Let the echo canceller know that a frame was just queued to the soundcard
         * @param st Echo canceller state
         * @param play Signal played to the speaker (received from far end)
        */
        [DllImport("libspeex.dll")]
        //void speex_echo_playback(SpeexEchoState *st, const spx_int16_t *play);
        public static extern void speex_echo_playback(IntPtr st, byte[] play);

        /** Reset the echo canceller to its original state 
         * @param st Echo canceller state
         */
        [DllImport("libspeex.dll")]
        //void speex_echo_state_reset(SpeexEchoState *st);
        public static extern void speex_echo_state_reset(IntPtr st);

        /** Used like the ioctl function to control the echo canceller parameters
         *
         * @param st Echo canceller state
         * @param request ioctl-type request (one of the SPEEX_ECHO_* macros)
         * @param ptr Data exchanged to-from function
         * @return 0 if no error, -1 if request in unknown
         */
        [DllImport("libspeex.dll")]
        //int speex_echo_ctl(SpeexEchoState *st, int request, void *ptr);
        public static extern int speex_echo_ctl(IntPtr st, int request, ref int ptr);
    }
}
