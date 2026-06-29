VPATH:= . shader_objects shaders
obj_dir:= shader_objects
shaders:= $(wildcard **/*.comp **/*.vert **/*.frag)
shader_objects:= $(patsubst %,%.spv, $(notdir $(shaders)))
shader_headers:= $(wildcard **/*.glsl)
all: run

spv: deps $(shader_objects)

run: spv
	dotnet run

deps: $(patsubst %,%.d, $(notdir $(shaders)))

%.d: %
	glslc -MM $< -o shader_objects/$@

%.spv: % 
	glslc $< -o shader_objects/$@
	
build: win-x64.zip linux-x64.zip osx-arm64.zip

%.zip: 
	mkdir -p builds
	dotnet publish --self-contained -r $(basename $@)
	cp -r bin/Release/net9.0/$(basename $@)/publish builds/$(basename $@)
	cd builds/$(basename $@); zip -r $@ ./**
	mv builds/$(basename $@)/$@ builds
	rm -rf builds/$(basename $@)

include $(wildcard $(obj_dir)/*.d)
	
.PHONY: spv build deps

