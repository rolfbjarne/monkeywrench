SOURCES = \
	$(wildcard *.cs)	\
	$(wildcard */*.cs)

TOPDIR = ..

LIBRARY = MonkeyWrench.Web.UI.dll
OUT_DIR = $(TOPDIR)/class/lib
OUT = $(OUT_DIR)/$(LIBRARY)

BUILDABLE_REFS = \
	$(OUT_DIR)/Npgsql.dll	\
	$(OUT_DIR)/MonkeyWrench.dll	\
	$(OUT_DIR)/MonkeyWrench.DataClasses.dll

REFERENCES = \
	$(foreach ref,$(BUILDABLE_REFS),-r:$(ref))	\
	-r:System.Data.dll	\
	-r:System.Web.dll	\
	-r:System.Web.Services.dll

$(OUT): $(SOURCES) $(BUILDABLE_REFS)
	gmcs -target:library -debug:full -out:$@ $(SOURCES) $(REFERENCES)

all: $(OUT)

clean:
	rm -f $(OUT)*
	rm -f bin/*.mdb bin/*.dll bin/*.pdb

publish: all
	mkdir -p bin
	# TODO: rsync to not trigger reloads if nothing has changed
	cp $(OUT_DIR)/Npgsql.dll* bin/
	cp $(OUT_DIR)/MonkeyWrench.dll* bin/
	cp $(OUT_DIR)/MonkeyWrench.DataClasses.dll* bin/
	cp $(OUT_DIR)/MonkeyWrench.Web.UI.dll* bin/

install: all
	@: