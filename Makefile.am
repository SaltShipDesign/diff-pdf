
bin_PROGRAMS = diff-pdf-b

diff_pdf_b_SOURCES = \
			diff-pdf.cpp \
			bmpviewer.cpp \
			bmpviewer.h \
			gutter.cpp \
			gutter.h

diff_pdf_b_CXXFLAGS = $(POPPLER_CFLAGS) $(WX_CXXFLAGS)
diff_pdf_b_LDADD = $(POPPLER_LIBS) $(WX_LIBS)

EXTRA_DIST = bootstrap gtk-zoom-in.xpm gtk-zoom-out.xpm README.md
